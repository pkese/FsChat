// https://github.com/markslides/markslides/blob/main/packages/markdown-it-mermaid/src/index.ts

import MarkdownIt from 'markdown-it';
import Token from 'markdown-it/lib/token';
import Renderer from 'markdown-it/lib/renderer';
//import mermaid, { type MermaidConfig } from 'mermaid';

const mermaidChart = async (mermaid: any, code: string) => {
    try {
        //let xx = await mermaid.parse(code, { suppressErrors: true });
        let { svg } = await mermaid.render(code, { suppressErrors: true });
        console.log('mermaid.parse', svg);
        //return `<div class="mermaid">${code}</div>`;
        return svg.outerHTML;
    } catch ({ str, hash }: any) {
        return `<pre>${str}</pre>`;
    }
};

// TODO: Inject this value from outside
const isDarkMode = false;

const markdownItMermaid = async (md: MarkdownIt, {mermaid, ...config}) => {
    //const mermaid = await getModule(urls.mermaid);
    //mermaid.default.initialize({
    console.log('config.mermaid', mermaid);
    mermaid.initialize({
        theme: isDarkMode ? 'default' : 'dark',
        darkMode: isDarkMode,
        //fontFamily: 'monospace',
        // fontFamily: 'ui-monospace',
        // altFontFamily: 'monospace',
        startOnLoad: true,
        ...config,
    });

    // @ts-ignore
    md.mermaid = mermaid;

    const original =
        md.renderer.rules.fence ||
        function (tokens, idx, options, env, self) {
            return self.renderToken(tokens, idx, options);
        };

    md.renderer.rules.fence = (
        tokens: Token[],
        idx: number,
        options: MarkdownIt.Options,
        env: any,
        self: Renderer
    ) => {
        const token = tokens[idx];
        const code = token.content.trim();
        if (token.info === 'mermaid') {
            return mermaidChart(mermaid, code);
        }

        return original(tokens, idx, options, env, self);
    };
};

export default markdownItMermaid;
