using FounderScout.Domain;

namespace FounderScout.Tests;

internal sealed class FounderScoutDomainTests
{
    [Test]
    public void CandidateLifecycleAcceptsDocumentedCaptureAnalysisAndManualInvitationPath()
    {
        CandidateStatus[] path =
        [
            CandidateStatus.Captured,
            CandidateStatus.Parsed,
            CandidateStatus.PendingAnalysis,
            CandidateStatus.Analyzing,
            CandidateStatus.Analyzed,
            CandidateStatus.Shortlisted,
            CandidateStatus.QueuedForInvite,
            CandidateStatus.MessageReviewed,
            CandidateStatus.ManuallySent,
            CandidateStatus.Accepted,
            CandidateStatus.CallScheduled,
            CandidateStatus.TrialProject,
            CandidateStatus.Selected,
        ];
        CandidateStatus current = CandidateStatus.Discovered;

        foreach (CandidateStatus requested in path)
        {
            FounderScoutTransitionResult<CandidateStatus> result = CandidateLifecycle.Validate(
                current,
                requested,
                "test.documented-path");
            Assert.That(result.IsAllowed, Is.True, $"{current} -> {requested}");
            current = result.State;
        }
    }

    [Test]
    public void CandidateLifecycleRejectsAutomaticInvitationSendShortcut()
    {
        FounderScoutTransitionResult<CandidateStatus> result = CandidateLifecycle.Validate(
            CandidateStatus.QueuedForInvite,
            CandidateStatus.ManuallySent,
            "test.no-auto-send");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsAllowed, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo("candidate.transition.illegal"));
            Assert.That(result.State, Is.EqualTo(CandidateStatus.QueuedForInvite));
        });
    }

    [Test]
    public void CandidateDtoContainsNoProtectedDemographicHealthOrPhotoFields()
    {
        string[] propertyNames = typeof(Candidate).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.That(propertyNames, Has.None.Matches<string>(name => name is
            "Age" or "BirthDate" or "Gender" or "Race" or "Ethnicity" or "Religion"
            or "Photo" or "Image" or "MaritalStatus" or "Health" or "Disability"));
    }

    [Test]
    public void AccountSnapshotEvaluationAndInvitationTransitionsAreExplicit()
    {
        FounderScoutTransitionResult<BrowserSessionStatus> disabled = BrowserAccountLifecycle.Validate(
            BrowserSessionStatus.Disabled,
            BrowserSessionStatus.Healthy,
            "test.disabled");
        FounderScoutTransitionResult<ProfileSnapshotStatus> snapshot = ProfileSnapshotLifecycle.Validate(
            ProfileSnapshotStatus.Captured,
            ProfileSnapshotStatus.PendingAnalysis,
            "test.skip-parse");
        FounderScoutTransitionResult<EvaluationStatus> evaluation = EvaluationLifecycle.Validate(
            EvaluationStatus.Claimed,
            EvaluationStatus.Completed,
            "test.complete");
        FounderScoutTransitionResult<InvitationDraftStatus> invitation = InvitationLifecycle.Validate(
            InvitationDraftStatus.Valid,
            InvitationDraftStatus.ManuallySent,
            "test.skip-review");

        Assert.Multiple(() =>
        {
            Assert.That(disabled.IsAllowed, Is.False);
            Assert.That(snapshot.IsAllowed, Is.False);
            Assert.That(evaluation.IsAllowed, Is.True);
            Assert.That(invitation.IsAllowed, Is.False);
        });
    }
}
