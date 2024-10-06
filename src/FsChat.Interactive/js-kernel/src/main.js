
import { urls, getModule } from './dynModules.js';
import injectMermaid from './makdownit-mermaid.js';

let md = null;
let mdWithHljs = null;
let mdWithMermaid = null;

let getMarkdown = (features) => {
    //console.log('features', features);
    if (features.mermaid) {
        if (!mdWithMermaid) {
            mdWithMermaid =
                Promise.all([
                    getModule(urls.markdownIt),
                    getModule(urls.hljs),
                    getModule(urls.mermaid),
                ]).then(([md, hljs, mermaid]) => {
                    md = md.default();
                    md.use(hljs.default);
                    md.use(injectMermaid, {mermaid: mermaid.default});
                    return md;
                });
        }
        return mdWithMermaid
    } else {
        if (!md) {
            md = getModule(urls.markdownIt).then(module => module.default());
        }
        if (features.hljs && !mdWithHljs) {
            mdWithHljs =
                Promise.all([
                    md,
                    getModule(urls.hljs),
                ]).then(([md, hljs]) => {
                    md.use(hljs.default);
                    return md;
                });
            md = mdWithHljs;
        }
        return md;
    }
}

window.renderMarkdown = async (text) => {
    let features;
    if (text.includes('```mermaid')) features = {mermaid:true, hljs:true}
    else if (mdWithHljs || text.includes('```')) features = {hljs:true}
    else features = {};
    let md = await getMarkdown(features);
    //console.log("md.for", features, md);
    let resolveMarkdown = null;
    let env = {
        postprocessMiddleware: new Promise((resolve) => {
            resolveMarkdown = resolve;
        })
    };
    let html = md.render(text, env); // Mermaid will inject (chain) itself into env.postprocessMiddleware
    resolveMarkdown(html);
    //console.log("render:", text, html);
    let patchedHtml = await env.postprocessMiddleware;
    return patchedHtml;
}

/*
window.markdownAll = () => {
    document
        .querySelectorAll('.markdown:not(.md-done)')
        .forEach(async (element) => {
            let text = element.textContent;
            element.classList.add('md-done');
            let html = await window.renderMarkdown(text);
            element.innerHTML = html;
        });
};
*/

window.appendMdChunk = async (tagId, newText) => {
    let el = document.getElementById(tagId);
    // extract current text from data
    let text = el.getAttribute('data-text') || '';
    text = text + newText;
    el.setAttribute('data-text', text);

    let features;
    if (mdWithHljs || text.includes('```')) features = {hljs: true}
    else features = {};

    let md = await getMarkdown(features);
    el.innerHTML = md.render(text);
}
