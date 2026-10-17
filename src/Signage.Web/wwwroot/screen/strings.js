// The player's few texts (R-64). Add a language by adding an object with the same keys.
const texts = {
    en: {
        productName: 'Signage',
        pairTitle: 'Pair this screen',
        pairInstruction: 'In the admin, open Screens → Pair screen and enter this code.',
        adminAddress: 'Admin address',
        previewSignIn: 'Sign in to the admin in this browser to preview this screen.',
        previewNotFound: 'This screen no longer exists.',
    },
};

const language = (navigator.language || 'en').slice(0, 2).toLowerCase();
const table = texts[language] ?? texts.en;

/** Looks up a text and fills {0}, {1}, … */
export function t(key, ...args) {
    const text = table[key] ?? texts.en[key] ?? key;
    return text.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''));
}
