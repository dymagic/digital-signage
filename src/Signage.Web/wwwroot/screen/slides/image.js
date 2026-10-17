// Image slides: decoded before the transition; a broken image is skipped (PLAYER_SPEC §5).
export function createImage(slide) {
    const img = document.createElement('img');
    img.alt = '';
    img.decoding = 'async';
    img.src = slide.url;
    const ready = img.decode().then(() => true, () => false);
    return { element: img, ready, release() { img.removeAttribute('src'); } };
}
