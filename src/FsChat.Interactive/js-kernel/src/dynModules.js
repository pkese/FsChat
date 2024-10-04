export const urls = {
    markdownIt: {
        js: 'https://cdn.jsdelivr.net/npm/markdown-it@14/+esm'
    },
    hljs: {
        js: 'https://cdn.jsdelivr.net/npm/markdown-it-highlightjs@4/+esm',
        css: 'https://cdn.jsdelivr.net/npm/highlight.js@11/styles/default.min.css'
    },
    mermaid: {
        js: 'https://cdn.jsdelivr.net/npm/mermaid@11/+esm'
    },
}

// load a module and return it as a promise
// use it to skip loading it if it already exists
let __loadModule = (scriptCfg) =>
    new Promise((resolve, reject) => {
        //console.log('loading', scriptCfg);
        let script = document.createElement('script');
        script.type = 'module';
        script.src = scriptCfg.js;
        script.onerror = reject;
        script.onload = () => import(/* @vite-ignore */ scriptCfg.js).then(resolve);
        document.head.appendChild(script);

        if (scriptCfg.css) {
            let link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = scriptCfg.css;
            link.defer = true;
            document.head.appendChild(link);
        }
    });

let __loadedModules = {};

export function getModule(scriptCfg) {
    const key = scriptCfg.js;
    if (__loadedModules[key]) {
        return __loadedModules[key];
    } else {
        let module = __loadModule(scriptCfg);
        __loadedModules[key] = module;
        return module;
    }
}
