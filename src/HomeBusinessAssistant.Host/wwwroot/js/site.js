(() => {
    const flash = document.querySelector('.flash');
    if (flash) flash.focus({ preventScroll: true });

    for (const form of document.querySelectorAll('form[data-confirm]')) {
        form.addEventListener('submit', event => {
            const message = form.getAttribute('data-confirm');
            if (message && !window.confirm(message)) event.preventDefault();
        });
    }

    const agentSelect = document.querySelector('[data-agent-select]');
    const commandSelect = document.querySelector('[data-command-select]');
    if (agentSelect && commandSelect) {
        agentSelect.addEventListener('change', () => {
            const selected = agentSelect.options[agentSelect.selectedIndex];
            let commands = [];
            try { commands = JSON.parse(selected?.dataset.commands || '[]'); } catch { commands = []; }
            commandSelect.replaceChildren(...commands.map(command => new Option(command, command)));
        });
    }

    const status = document.querySelector('[data-run-status-url]');
    if (status) {
        const url = status.getAttribute('data-run-status-url');
        const refresh = async () => {
            try {
                const response = await fetch(url, { headers: { Accept: 'application/json' }, cache: 'no-store' });
                if (!response.ok) return;
                const data = await response.json();
                status.textContent = data.status;
                if (!data.active) window.clearInterval(timer);
            } catch { /* The full page remains authoritative. */ }
        };
        const timer = window.setInterval(refresh, 5000);
    }

    const autoRefresh = document.querySelector('[data-auto-refresh-seconds]');
    if (autoRefresh) {
        const seconds = Number.parseInt(autoRefresh.getAttribute('data-auto-refresh-seconds') || '', 10);
        if (Number.isFinite(seconds) && seconds >= 1 && seconds <= 60) {
            window.setTimeout(() => window.location.reload(), seconds * 1000);
        }
    }

    for (const button of document.querySelectorAll('[data-copy-source]')) {
        button.addEventListener('click', async () => {
            const source = document.getElementById(button.getAttribute('data-copy-source'));
            if (!source) return;
            const original = button.textContent;
            try {
                await navigator.clipboard.writeText(source.textContent || '');
                button.textContent = 'Copied';
            } catch {
                button.textContent = 'Copy failed';
            }
            window.setTimeout(() => { button.textContent = original; }, 1800);
        });
    }
})();
