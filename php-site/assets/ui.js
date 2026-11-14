'use strict';

// JavaScript отвечает только за интерфейс; запросы выполняет PHP через cURL.
document.querySelectorAll('form[data-busy], form[data-confirm]').forEach((form) => {
    form.addEventListener('submit', (event) => {
        if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            event.preventDefault();
            return;
        }
        if (form.dataset.submitting === 'true') {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = 'true';
        form.classList.add('is-submitting');
        form.setAttribute('aria-busy', 'true');
        const submitter = event.submitter || form.querySelector('[type="submit"]');
        if (submitter && form.dataset.busy) {
            submitter.dataset.originalText = submitter.textContent;
            submitter.textContent = form.dataset.busy;
        }
    });
});

// При возврате браузером восстановим доступность кнопок.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('form[data-submitting="true"]').forEach((form) => {
        delete form.dataset.submitting;
        form.classList.remove('is-submitting');
        form.removeAttribute('aria-busy');
        form.querySelectorAll('[data-original-text]').forEach((button) => {
            button.textContent = button.dataset.originalText;
            delete button.dataset.originalText;
        });
    });
});

const errorSummary = document.getElementById('form-errors');
if (errorSummary) errorSummary.focus({ preventScroll: false });

const incidentForm = document.querySelector('.incident-form');
if (incidentForm) {
    const start = incidentForm.elements.namedItem('beginTime');
    const end = incidentForm.elements.namedItem('endTime');
    const validateDates = () => {
        end.setCustomValidity(start.value && end.value && end.value < start.value
            ? 'Окончание не может быть раньше начала.' : '');
    };
    start.addEventListener('input', validateDates);
    end.addEventListener('input', validateDates);
    validateDates();
}
