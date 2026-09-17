(() => {
    "use strict";

    document.querySelectorAll("[data-id-flip]").forEach((button) => {
        button.addEventListener("click", () => {
            const card = document.getElementById(button.getAttribute("aria-controls"));
            if (!card) return;

            const showingBack = card.classList.toggle("is-flipped");
            button.setAttribute("aria-pressed", showingBack.toString());
            const label = button.querySelector("span");
            if (label) label.textContent = showingBack ? button.dataset.showFront : button.dataset.showBack;
        });
    });

    document.querySelectorAll("[data-id-number-toggle]").forEach((button) => {
        button.addEventListener("click", () => {
            const number = button.parentElement?.querySelector("[data-id-number]");
            if (!number) return;

            const revealed = button.getAttribute("aria-pressed") === "true";
            number.textContent = revealed ? number.dataset.maskedNumber : number.dataset.fullNumber;
            button.setAttribute("aria-pressed", (!revealed).toString());
            button.textContent = revealed ? button.dataset.showText : button.dataset.hideText;
        });
    });

    const dialog = document.getElementById("idZoomDialog");
    if (!(dialog instanceof HTMLDialogElement)) return;

    const image = dialog.querySelector("[data-id-zoom-image]");
    const title = dialog.querySelector("#idZoomTitle");
    const level = dialog.querySelector("[data-id-zoom-level]");
    const sideButton = dialog.querySelector("[data-id-zoom-side]");
    const viewport = dialog.querySelector(".id-zoom-viewport");
    let scale = 1;
    let side = "front";
    let frontUrl = "";
    let backUrl = "";

    const render = () => {
        image.style.width = `${scale * 100}%`;
        level.textContent = `${Math.round(scale * 100)}%`;
    };

    const setScale = (nextScale) => {
        scale = Math.min(3, Math.max(1, nextScale));
        render();
    };

    const setSide = (nextSide) => {
        side = nextSide;
        image.src = side === "back" ? backUrl : frontUrl;
        image.alt = `${side === "back" ? "Back" : "Front"} of ${title.textContent}`;
        sideButton.innerHTML = `<i class="bi bi-arrow-repeat" aria-hidden="true"></i> ${side === "back" ? sideButton.dataset.showFront : sideButton.dataset.showBack}`;
        setScale(1);
        viewport.scrollTo({ top: 0, left: 0 });
    };

    document.querySelectorAll("[data-id-zoom]").forEach((button) => {
        button.addEventListener("click", () => {
            frontUrl = button.dataset.frontUrl || "";
            backUrl = button.dataset.backUrl || "";
            title.textContent = button.dataset.idTitle || "ID image";
            sideButton.hidden = !backUrl;
            setSide("front");
            dialog.showModal();
        });
    });

    dialog.querySelector("[data-id-zoom-close]")?.addEventListener("click", () => dialog.close());
    dialog.querySelector("[data-id-zoom-in]")?.addEventListener("click", () => setScale(scale + 0.25));
    dialog.querySelector("[data-id-zoom-out]")?.addEventListener("click", () => setScale(scale - 0.25));
    dialog.querySelector("[data-id-zoom-reset]")?.addEventListener("click", () => setScale(1));
    sideButton?.addEventListener("click", () => setSide(side === "front" ? "back" : "front"));

    viewport?.addEventListener("wheel", (event) => {
        event.preventDefault();
        setScale(scale + (event.deltaY < 0 ? 0.25 : -0.25));
    }, { passive: false });

    dialog.addEventListener("click", (event) => {
        if (event.target === dialog) dialog.close();
    });

    dialog.addEventListener("close", () => {
        image.removeAttribute("src");
        setScale(1);
    });
})();
