(() => {
    const files = document.getElementById('batchFiles');
    const container = document.getElementById('batchItems');
    const template = document.getElementById('batchItemTemplate');
    const encounter = document.getElementById('ClinicalEncounterId');
    if (!files || !container || !template || !encounter) return;
    const filterChoices = () => container.querySelectorAll('select[data-field]').forEach(select => {
        [...select.options].forEach(option => { option.hidden = !!option.dataset.encounter && option.dataset.encounter !== encounter.value; });
        if (select.selectedOptions[0]?.hidden) select.value = '';
        const description = select.closest('fieldset').querySelector('input[data-field]');
        description.required = !select.value;
    });
    files.addEventListener('change', () => {
        const selection = [...files.files];
        files.setCustomValidity(selection.length > 10 || selection.some(file => file.size > 10 * 1024 * 1024) || selection.reduce((sum, file) => sum + file.size, 0) > 25 * 1024 * 1024
            ? 'Choose up to 10 PDFs, up to 10 MiB each and 25 MiB combined.' : '');
        container.replaceChildren();
        selection.forEach((file, index) => {
            const item = template.content.cloneNode(true);
            item.querySelector('[data-file-name]').textContent = `PDF ${index + 1}: ${file.name}`;
            item.querySelectorAll('[data-field]').forEach(field => {
                field.name = `Items[${index}].${field.dataset.field}`;
                field.id = `batch-${index}-${field.dataset.field}`;
            });
            item.querySelector('[data-target-label]').htmlFor = `batch-${index}-ExistingRequest`;
            item.querySelector('[data-description-label]').htmlFor = `batch-${index}-Description`;
            item.querySelector('select').addEventListener('change', filterChoices);
            container.append(item);
        });
        filterChoices();
    });
    encounter.addEventListener('change', filterChoices);
})();
