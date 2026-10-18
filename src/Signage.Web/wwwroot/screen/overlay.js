// Overlays above everything the screen shows, slides and single video alike (PLAYER_SPEC §7):
// - the info overlay (R-30): `Name · IP` bottom-left when the global setting is on, with a tiny dim dot while offline;
// - identification (R-31, R-32): the name in large text, with a pulsing red border for "identify one". It lasts for the
//   event's seconds; a new event restarts the timer; then it fades out.
import { config, ms } from './config.js';

export class Overlays {
    /** @param {HTMLElement} stage */
    constructor(stage) {
        this.info = document.createElement('div');
        this.info.className = 'info-overlay';
        this.info.dataset.testid = 'info-overlay';
        this.info.hidden = true;

        this.identifyEl = document.createElement('div');
        this.identifyEl.className = 'identify';
        this.identifyEl.dataset.testid = 'identify';
        this.nameEl = document.createElement('p');
        this.nameEl.className = 'identify-name';
        this.nameEl.dataset.testid = 'identify-name';
        this.identifyEl.append(this.nameEl);

        stage.append(this.info, this.identifyEl);
        this.name = '';
        this.timer = 0;
        this.fadeTimer = 0;
    }

    /** Applies a manifest at once (overlay changes don't wait for a transition, PLAYER_SPEC §9). */
    update(manifest) {
        this.name = manifest?.screen?.name ?? '';
        this.nameEl.textContent = this.name;
        this.nameEl.hidden = !this.name; // no empty pill before the first manifest
        const show = Boolean(manifest?.settings?.showInfoOverlay);
        this.info.hidden = !show;
        this.info.textContent = [this.name, manifest?.observedIp].filter(Boolean).join(' · ');
    }

    setOnline(online) {
        this.info.classList.toggle('offline', !online);
    }

    /** Shows the name (and the red border for one screen) for `seconds`, restarting any running identification. */
    identify(seconds, withBorder) {
        this.nameEl.hidden = !this.name;
        clearTimeout(this.timer);
        clearTimeout(this.fadeTimer);
        this.identifyEl.classList.toggle('with-border', withBorder);
        this.identifyEl.classList.remove('fading');
        this.identifyEl.classList.add('active');
        document.body.dataset.identify = withBorder ? 'one' : 'all';
        this.timer = setTimeout(() => {
            this.identifyEl.classList.add('fading');
            this.fadeTimer = setTimeout(() => {
                this.identifyEl.classList.remove('active', 'fading', 'with-border');
                delete document.body.dataset.identify;
            }, ms(config.identifyFadeMs));
        }, ms(seconds * 1000));
    }
}
