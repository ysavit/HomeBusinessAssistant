using System.Text.Json;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Host.Pages.Schedules;

/// <summary>Typed schedule editor and real schedule-calculator preview.</summary>
[AutoValidateAntiforgeryToken]
public sealed class EditModel(HostManagementComposition management) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Gets or sets typed schedule fields.</summary>
    [BindProperty]
    public ScheduleForm Form { get; set; } = new();

    /// <summary>Gets installed agents.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Gets all manifest command choices.</summary>
    public IReadOnlyList<string> AvailableCommands { get; private set; } = [];

    /// <summary>Gets the real next-five preview.</summary>
    public IReadOnlyList<DateTimeOffset> Preview { get; private set; } = [];

    /// <summary>Loads a new or existing schedule editor.</summary>
    public async Task<IActionResult> OnGetAsync(Guid? id, string? agentId, CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        if (id.HasValue)
        {
            ManagementScheduleDetail? detail = await management.Queries!.GetScheduleAsync(id.Value, cancellationToken).ConfigureAwait(false);
            if (detail is null)
            {
                return NotFound();
            }

            Form = ScheduleForm.From(detail.Schedule.Schedule);
            RefreshAvailableCommands();
        }
        else
        {
            ManagementAgentItem? selected = AgentId.TryParse(agentId, out AgentId parsed)
                ? Agents.FirstOrDefault(item => item.Definition.Id == parsed)
                : Agents.FirstOrDefault(item => item.Definition.Enabled && item.ConfigurationRevision.HasValue);
            selected ??= Agents.Count == 0 ? null : Agents[0];
            if (selected is not null)
            {
                Form.AgentId = selected.Definition.Id.Value;
                string[] commands = ParseCommands(selected.Definition.SupportedCommandsJson);
                Form.CommandName = commands.Length == 0 ? "run" : commands[0];
                Form.ConcurrencyPolicy = selected.Definition.DefaultConcurrencyPolicy;
                RefreshAvailableCommands();
            }
        }

        return Page();
    }

    /// <summary>Calculates next due slots without saving.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken).ConfigureAwait(false) || management.Commands is null)
        {
            return NotFound();
        }

        try
        {
            AgentScheduleRecord candidate = BuildSchedule();
            Preview = await management.Commands.PreviewScheduleAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentScheduleValidationException exception)
        {
            AddScheduleErrors(exception.Errors);
        }
        catch (ScheduleDefinitionValidationException exception)
        {
            AddScheduleErrors(exception.Errors);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return Page();
    }

    /// <summary>Validates, persists, audits, and reconciles a schedule.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken).ConfigureAwait(false) || management.Commands is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            AgentScheduleRecord candidate = BuildSchedule();
            AgentScheduleRecord saved = await management.Commands.SaveScheduleAsync(
                candidate,
                Form.ExpectedConcurrencyToken,
                "local-web",
                Guid.NewGuid(),
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = $"Schedule '{saved.Name}' was saved and schedule/wake state was reconciled.";
            return RedirectToPage("/Schedules/Details", new { id = saved.Id });
        }
        catch (AgentScheduleValidationException exception)
        {
            AddScheduleErrors(exception.Errors);
        }
        catch (ScheduleDefinitionValidationException exception)
        {
            AddScheduleErrors(exception.Errors);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "The schedule changed after this editor was opened. Reload before saving again.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return Page();
    }

    private async ValueTask<bool> LoadChoicesAsync(CancellationToken cancellationToken)
    {
        if (management.Queries is null)
        {
            return false;
        }

        Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        RefreshAvailableCommands();
        return true;
    }

    private void RefreshAvailableCommands()
    {
        ManagementAgentItem? selected = Agents.FirstOrDefault(item => item.Definition.Id.Value == Form.AgentId);
        AvailableCommands = selected is null
            ? []
            : ParseCommands(selected.Definition.SupportedCommandsJson).Order(StringComparer.Ordinal).ToArray();
    }

    private AgentScheduleRecord BuildSchedule()
    {
        if (!AgentId.TryParse(Form.AgentId, out AgentId agentId))
        {
            throw new ArgumentException("Select an installed agent.");
        }

        ScheduleDefinition definition = Form.Kind switch
        {
            ScheduleKind.Manual => new ManualScheduleDefinition(),
            ScheduleKind.OneTime when Form.OneTimeLocal.HasValue => OneTimeScheduleDefinition.AtLocal(Form.OneTimeLocal.Value),
            ScheduleKind.Daily => new DailyScheduleDefinition(ParseLocalTime()),
            ScheduleKind.SelectedWeekdays => new WeekdayScheduleDefinition(Form.Weekdays, ParseLocalTime()),
            ScheduleKind.FixedInterval when Form.AnchorUtc.HasValue => new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(Form.IntervalMinutes), Form.AnchorUtc.Value),
            ScheduleKind.FixedDelay => new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(Form.DelayMinutes), Form.StartImmediately ? null : Form.InitialDueUtc, Form.StartImmediately),
            _ => throw new ArgumentException("Complete the timing fields for the selected schedule type."),
        };
        DateTimeOffset nowUtc = (management.TimeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
        bool wakeWindow = agentId == WakeRemoteDefaults.AgentId && Form.CommandName == "run";
        string arguments = wakeWindow
            ? JsonSerializer.Serialize(new WakeRemoteWindowTemplate(
                checked(Form.WindowDurationMinutes * 60),
                Form.RemoteProviderRequired,
                Form.MisfirePolicy == MisfirePolicy.RunImmediately), JsonOptions)
            : "{}";
        return new(
            Form.Id ?? Guid.NewGuid(),
            agentId,
            Form.Name,
            Form.CommandName,
            arguments,
            Form.Kind,
            ScheduleDefinitionJson.Serialize(definition),
            Form.TimeZoneId,
            Form.MisfirePolicy,
            Form.ConcurrencyPolicy,
            TimeSpan.FromMinutes(Form.TimeoutMinutes),
            TimeSpan.FromMinutes(Form.MisfireGraceMinutes),
            RetryPolicyJson.Serialize(new(
                Form.MaximumRetries,
                TimeSpan.FromMinutes(Form.InitialRetryMinutes),
                TimeSpan.FromMinutes(Form.MaximumRetryMinutes))),
            Form.Kind == ScheduleKind.Manual ? WakePolicy.Never : Form.WakePolicy,
            PinnedConfigurationRevisionId: null,
            AllowDisabledAgent: false,
            Form.IsEnabled,
            Form.IsPaused,
            Form.IsPaused ? Form.PausedUntilUtc : null,
            Form.CreatedAtUtc ?? nowUtc,
            nowUtc,
            Form.ExpectedConcurrencyToken ?? 0);
    }

    private TimeOnly ParseLocalTime() => TimeOnly.TryParse(Form.LocalTime, out TimeOnly value)
        ? value
        : throw new ArgumentException("Enter a valid local wall-clock time.");

    private void AddScheduleErrors(IEnumerable<ScheduleValidationError> errors)
    {
        foreach (ScheduleValidationError error in errors)
        {
            string key = error.Path switch
            {
                "$.agentId" => "Form.AgentId",
                "$.name" => "Form.Name",
                "$.commandName" => "Form.CommandName",
                "$.timeZoneId" => "Form.TimeZoneId",
                "$.localTime" => "Form.LocalTime",
                "$.days" => "Form.Weekdays",
                "$.timeout" => "Form.TimeoutMinutes",
                "$.misfireGracePeriod" => "Form.MisfireGraceMinutes",
                _ => string.Empty,
            };
            ModelState.AddModelError(key, error.Message);
        }
    }

    private static string[] ParseCommands(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Typed schedule fields; no arbitrary executable arguments are accepted.</summary>
    public sealed class ScheduleForm
    {
        /// <summary>Gets or sets the existing schedule identifier.</summary>
        public Guid? Id { get; set; }
        /// <summary>Gets or sets the existing optimistic concurrency token.</summary>
        public long? ExpectedConcurrencyToken { get; set; }
        /// <summary>Gets or sets the original creation time.</summary>
        public DateTimeOffset? CreatedAtUtc { get; set; }
        /// <summary>Gets or sets the installed agent ID.</summary>
        public string AgentId { get; set; } = string.Empty;
        /// <summary>Gets or sets the schedule name.</summary>
        public string Name { get; set; } = "New schedule";
        /// <summary>Gets or sets a manifest command.</summary>
        public string CommandName { get; set; } = "run";
        /// <summary>Gets or sets the schedule kind.</summary>
        public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
        /// <summary>Gets or sets the Windows time-zone ID.</summary>
        public string TimeZoneId { get; set; } = SchedulingDefaults.DefaultWindowsTimeZoneId;
        /// <summary>Gets or sets the local wall-clock time.</summary>
        public string LocalTime { get; set; } = "08:00";
        /// <summary>Gets or sets selected weekdays.</summary>
        public List<DayOfWeek> Weekdays { get; set; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        /// <summary>Gets or sets one local date/time.</summary>
        public DateTime? OneTimeLocal { get; set; } = DateTime.Today.AddDays(1).AddHours(8);
        /// <summary>Gets or sets a fixed-interval UTC anchor.</summary>
        public DateTimeOffset? AnchorUtc { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Gets or sets fixed interval minutes.</summary>
        public int IntervalMinutes { get; set; } = 60;
        /// <summary>Gets or sets fixed delay minutes.</summary>
        public int DelayMinutes { get; set; } = 60;
        /// <summary>Gets or sets whether fixed delay starts immediately.</summary>
        public bool StartImmediately { get; set; } = true;
        /// <summary>Gets or sets an optional fixed-delay initial UTC time.</summary>
        public DateTimeOffset? InitialDueUtc { get; set; }
        /// <summary>Gets or sets misfire policy.</summary>
        public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.RunNextScheduled;
        /// <summary>Gets or sets concurrency policy.</summary>
        public ConcurrencyPolicy ConcurrencyPolicy { get; set; } = ConcurrencyPolicy.Forbid;
        /// <summary>Gets or sets wake policy.</summary>
        public WakePolicy WakePolicy { get; set; } = WakePolicy.Never;
        /// <summary>Gets or sets timeout minutes.</summary>
        public int TimeoutMinutes { get; set; } = 60;
        /// <summary>Gets or sets misfire grace minutes.</summary>
        public int MisfireGraceMinutes { get; set; } = 15;
        /// <summary>Gets or sets maximum retries.</summary>
        public int MaximumRetries { get; set; }
        /// <summary>Gets or sets initial retry delay minutes.</summary>
        public int InitialRetryMinutes { get; set; } = 1;
        /// <summary>Gets or sets maximum retry delay minutes.</summary>
        public int MaximumRetryMinutes { get; set; } = 60;
        /// <summary>Gets or sets Wake Remote window duration minutes.</summary>
        public int WindowDurationMinutes { get; set; } = 60;
        /// <summary>Gets or sets whether the Wake Remote provider is required.</summary>
        public bool RemoteProviderRequired { get; set; } = true;
        /// <summary>Gets or sets enablement.</summary>
        public bool IsEnabled { get; set; } = true;
        /// <summary>Gets or sets paused state.</summary>
        public bool IsPaused { get; set; }
        /// <summary>Gets or sets optional timed pause.</summary>
        public DateTimeOffset? PausedUntilUtc { get; set; }

        /// <summary>Maps an existing durable schedule into typed editor fields.</summary>
        public static ScheduleForm From(AgentScheduleRecord schedule)
        {
            var form = new ScheduleForm
            {
                Id = schedule.Id,
                ExpectedConcurrencyToken = schedule.ConcurrencyToken,
                CreatedAtUtc = schedule.CreatedAtUtc,
                AgentId = schedule.AgentId.Value,
                Name = schedule.Name,
                CommandName = schedule.CommandName,
                Kind = schedule.Kind,
                TimeZoneId = schedule.TimeZoneId,
                MisfirePolicy = schedule.MisfirePolicy,
                ConcurrencyPolicy = schedule.ConcurrencyPolicy,
                WakePolicy = schedule.WakePolicy,
                TimeoutMinutes = Math.Max(1, (int)schedule.Timeout.TotalMinutes),
                MisfireGraceMinutes = (int)schedule.MisfireGracePeriod.TotalMinutes,
                IsEnabled = schedule.IsEnabled,
                IsPaused = schedule.IsPaused,
                PausedUntilUtc = schedule.PausedUntilUtc,
            };
            ApplyDefinition(form, ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind).Definition);
            (RetryPolicyDefinition? retry, _) = RetryPolicyJson.Parse(schedule.RetryPolicyJson);
            if (retry is not null)
            {
                form.MaximumRetries = retry.MaximumRetries;
                form.InitialRetryMinutes = Math.Max(1, (int)retry.InitialDelay.TotalMinutes);
                form.MaximumRetryMinutes = Math.Max(1, (int)retry.MaximumDelay.TotalMinutes);
            }

            if (schedule.AgentId == WakeRemoteDefaults.AgentId && schedule.CommandName == "run")
            {
                try
                {
                    WakeRemoteWindowTemplate? template = JsonSerializer.Deserialize<WakeRemoteWindowTemplate>(schedule.ArgumentsJson, JsonOptions);
                    if (template is not null)
                    {
                        form.WindowDurationMinutes = template.WindowDurationSeconds / 60;
                        form.RemoteProviderRequired = template.RemoteProviderRequired;
                    }
                }
                catch (JsonException)
                {
                    // The validator will report invalid persisted arguments on the next save.
                }
            }

            return form;
        }

        private static void ApplyDefinition(ScheduleForm form, ScheduleDefinition? definition)
        {
            switch (definition)
            {
                case OneTimeScheduleDefinition value:
                    form.OneTimeLocal = value.Semantics == ScheduleTimeSemantics.Local ? value.LocalDateTime : value.UtcDateTime.LocalDateTime;
                    break;
                case DailyScheduleDefinition value:
                    form.LocalTime = value.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case WeekdayScheduleDefinition value:
                    form.LocalTime = value.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    form.Weekdays = value.Days.ToList();
                    break;
                case FixedIntervalScheduleDefinition value:
                    form.IntervalMinutes = (int)value.Interval.TotalMinutes;
                    form.AnchorUtc = value.AnchorUtc;
                    break;
                case FixedDelayScheduleDefinition value:
                    form.DelayMinutes = (int)value.Delay.TotalMinutes;
                    form.StartImmediately = value.StartImmediately;
                    form.InitialDueUtc = value.InitialDueAtUtc;
                    break;
            }
        }
    }
}
