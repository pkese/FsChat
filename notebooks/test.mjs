// test.js
import { JSDOM } from 'jsdom';

// Create a new JSDOM instance
const dom = new JSDOM(`<!DOCTYPE html><p>Hello world</p>`);

// Set up global window and document objects
global.window = dom.window;
global.document = dom.window.document;

// Now you can use window and document as if you were in a browser environment
console.log(document.querySelector('p').textContent); // Outputs: Hello world


// ------------------------------------------


import markdownit from 'markdown-it';
//import mermaidPlugin from "@agoose77/markdown-it-mermaid";
import mermaidPlugin from "./markdown-it-mermaid.js";

//console.log("loading MarkdownIt");


// const regex = /(?:\r?\n)```mermaid\r?\n([\s\S]*?)\r?\n```/g;


let md = markdownit();
md.use(mermaidPlugin);

let text1 = `
# Hello World

\`\`\`mermaid
graph TD;
    A-->B;
    A-->C;
    B-->D;
    C-->D;
\`\`\``;

let text2 = `
# Hello World

\`\`\`mermaid
graph TD;
    A-->B;
    A-->C;
    B-->D;
    C-->D;
`;

console.log(md.render(text1, {textLines:text1.split('\n')}));
console.log(md.render(text2, {textLines:text2.split('\n')}));

