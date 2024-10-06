
const renderTimeout = (ms) => {
    return new Promise((resolve) => {
        window.requestAnimationFrame(resolve);
    });
};


// Define interface to await readiness of import
export default async function injectMermaid(md, {mermaid, ...options}) {
    //console.log('Mermaid', mermaid);
    // Setup Mermaid
    mermaid.initialize({ securityLevel: 'loose', ...options });
    function getLangName(info) {
        return info.split(/\s+/g)[0];
    }

    let idCounter = 0;

    // Store reference to original renderer.
    let defaultFenceRenderer = md.renderer.rules.fence;
    // Render custom code types as SVGs, letting the fence parser do all the heavy lifting.
    function customFenceRenderer(tokens, idx, options, env, slf) {

        let token = tokens[idx];
        let info = token.info.trim();
        let langName = info ? getLangName(info) : "";
        if (['mermaid', '{mermaid}'].indexOf(langName) === -1) {
            if (defaultFenceRenderer !== undefined) {
                return defaultFenceRenderer(tokens, idx, options, env, slf);
            }
            // Missing fence renderer!
            return "";
        }
        let offscreen = document.createElement('div');
        offscreen.id = `mermaid-${idCounter++}`;
        document.body.appendChild(offscreen);
        offscreen.style.display = "none";

        // Create element to render into
        const element_id = `mermaid-${idCounter++}`;
        let placeholderDiv = `<div id='${element_id}' class='mermaid'></div>`;

        let renderedSvgAsync = mermaid.render(offscreen.id, token.content);

        env.postprocessMiddleware =
            env.postprocessMiddleware.then(async (text) => {
                let { svg, bindingFunc } = await renderedSvgAsync;
                offscreen.remove();
                return text.replace(placeholderDiv, `<div class='mermaid-blk' style='background-color:white;'>\n${svg}\n</div>`);
            });

        // workaround for dark mode vscode

        /*
        { // Render with Mermaid asynchronously
            renderTimeout()
            .then(() => mermaid.render(offscreen.id, token.content))
            .then(({ svg, bindingFunc }) => {
                let container = document.getElementById(element_id);
                container.innerHTML = svg;
                if (bindingFunc) {
                    bindingFunc(container);
                }
                offscreen.remove();
            });
        }
        */
        return placeholderDiv;
    }
    md.renderer.rules.fence = customFenceRenderer;
}

