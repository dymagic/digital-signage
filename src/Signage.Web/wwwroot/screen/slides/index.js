// Slide factories. Each returns { element, ready: Promise<boolean>, release() }; ready resolves false when the slide
// can't be shown (it is then skipped). Video slides add start(), finished, setLoop() and progress() (video.js).
import { createImage } from './image.js';
import { createVideo } from './video.js';
import { createHtml } from './html.js';
import { createWebPage } from './webpage.js';

const factories = { Image: createImage, Video: createVideo, Html: createHtml, WebPage: createWebPage };

export function createSlide(slide) {
    const create = factories[slide.type];
    if (!create) {
        return { element: document.createElement('div'), ready: Promise.resolve(false), release() {} };
    }
    return create(slide);
}
