// Show/hide toggles for password inputs: <button class="pw-toggle" aria-controls="inputId">.
(() => {
    document.querySelectorAll(".pw-toggle").forEach((btn) => {
        btn.addEventListener("click", () => {
            const input = document.getElementById(btn.getAttribute("aria-controls"));
            if (!input) return;
            const isPassword = input.getAttribute("type") === "password";
            input.setAttribute("type", isPassword ? "text" : "password");
            btn.setAttribute("aria-pressed", isPassword ? "true" : "false");
            btn.setAttribute("aria-label", isPassword ? "Hide password" : "Show password");
            const icon = btn.querySelector("i");
            if (icon) icon.className = isPassword ? "bi bi-eye-slash" : "bi bi-eye";
        });
    });
})();
