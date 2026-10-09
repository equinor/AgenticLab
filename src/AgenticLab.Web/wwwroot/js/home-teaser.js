const rotationDelay = 7000;

class AgenticHomeTeaser extends HTMLElement {
    #events;
    #timer;
    #slides = [];
    #controls;
    #toggle;
    #counter;
    #pauseIcon;
    #playIcon;
    #index = 0;
    #paused = true;
    #hovered = false;
    #pointerPause;

    connectedCallback() {
        if (this.#events) return;
        const events = this.#events = new AbortController();
        queueMicrotask(() => {
            if (this.isConnected && !events.signal.aborted) this.#initialize(events.signal);
        });
    }

    disconnectedCallback() {
        this.#events?.abort();
        this.#events = undefined;
        clearTimeout(this.#timer);
        this.#timer = undefined;
        this.removeAttribute("data-ready");
        this.removeAttribute("aria-roledescription");
        for (const slide of this.#slides) {
            for (const attribute of ["data-active", "aria-hidden", "aria-label", "aria-roledescription", "role"])
                slide.removeAttribute(attribute);
            slide.inert = false;
        }
        if (this.#controls) this.#controls.hidden = true;
        this.#slides = [];
    }

    #initialize(signal) {
        this.#slides = [...this.querySelectorAll("[data-teaser-slide]")];
        this.#controls = this.querySelector("[data-teaser-controls]");
        this.#toggle = this.querySelector('[data-teaser-action="toggle"]');
        this.#counter = this.querySelector("[data-teaser-count]");
        this.#pauseIcon = this.querySelector("[data-teaser-pause]");
        this.#playIcon = this.querySelector("[data-teaser-play]");
        if (this.#slides.length < 2 || !this.#controls || !this.#toggle || !this.#counter || !this.#pauseIcon || !this.#playIcon) return;

        const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
        this.#index = 0;
        this.#paused = motion.matches || this.contains(document.activeElement);
        this.#hovered = this.matches(":hover");
        this.#pointerPause = undefined;
        this.setAttribute("aria-roledescription", "carousel");
        this.#slides.forEach((slide, index) => {
            slide.setAttribute("role", "group");
            slide.setAttribute("aria-roledescription", "slide");
            slide.setAttribute("aria-label", `${index + 1} of ${this.#slides.length}`);
        });

        this.#toggle.addEventListener("pointerdown", () => this.#pointerPause = !this.#paused, { signal });
        this.#toggle.addEventListener("pointercancel", () => this.#pointerPause = undefined, { signal });
        this.addEventListener("click", event => {
            const button = event.target instanceof Element ? event.target.closest("[data-teaser-action]") : null;
            if (!button || !this.contains(button)) return;
            const action = button.dataset.teaserAction;
            if (action === "toggle") {
                this.#paused = event.detail > 0 && this.#pointerPause !== undefined ? this.#pointerPause : !this.#paused;
                this.#pointerPause = undefined;
                this.#render();
            } else if (action === "previous" || action === "next") {
                this.#paused = true;
                this.#advance(action === "next" ? 1 : -1);
            }
        }, { signal });
        this.addEventListener("keydown", event => {
            if (event.altKey || event.ctrlKey || event.metaKey || !["ArrowLeft", "ArrowRight"].includes(event.key)) return;
            event.preventDefault();
            this.#paused = true;
            this.#advance(event.key === "ArrowRight" ? 1 : -1);
        }, { signal });
        this.addEventListener("focusin", () => {
            this.#paused = true;
            this.#render();
        }, { signal });
        this.addEventListener("pointerenter", event => {
            if (event.pointerType === "touch") return;
            this.#hovered = true;
            this.#schedule();
        }, { signal });
        this.addEventListener("pointerleave", () => {
            this.#hovered = false;
            this.#schedule();
        }, { signal });
        document.addEventListener("visibilitychange", () => this.#schedule(), { signal });
        motion.addEventListener("change", event => {
            if (event.matches) this.#paused = true;
            this.#render();
        }, { signal });

        this.setAttribute("data-ready", "");
        this.#controls.hidden = false;
        this.#render();
    }

    #advance(direction) {
        this.#index = (this.#index + direction + this.#slides.length) % this.#slides.length;
        this.#render();
    }

    #render() {
        this.#slides.forEach((slide, index) => {
            const active = index === this.#index;
            slide.toggleAttribute("data-active", active);
            slide.setAttribute("aria-hidden", String(!active));
            slide.inert = !active;
        });
        this.#counter.textContent = `${this.#index + 1} / ${this.#slides.length}`;
        const label = this.#paused ? "Play rotation" : "Pause rotation";
        this.#toggle.setAttribute("aria-label", label);
        this.#toggle.title = label;
        this.#pauseIcon.hidden = this.#paused;
        this.#playIcon.hidden = !this.#paused;
        this.#schedule();
    }

    #schedule() {
        clearTimeout(this.#timer);
        this.#timer = undefined;
        if (this.isConnected && !this.#paused && !this.#hovered && !document.hidden)
            this.#timer = setTimeout(() => this.#advance(1), rotationDelay);
    }
}

if (!customElements.get("agentic-home-teaser"))
    customElements.define("agentic-home-teaser", AgenticHomeTeaser);