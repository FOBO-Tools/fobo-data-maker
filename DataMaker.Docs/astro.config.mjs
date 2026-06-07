// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Project page under the FOBO-Tools org. If you later attach a custom domain,
// update `site` and drop `base`.
export default defineConfig({
  site: 'https://fobo-tools.github.io',
  base: '/fobo-data-maker',
  integrations: [
    starlight({
      title: 'Data Maker',
      description:
        'Build clients and renderers for Data Maker forms: submit records, render .dmf bundles, and read the form schema.',
      logo: { src: './src/assets/fobo-logo.svg', alt: 'FOBO' },
      customCss: ['./src/styles/fobo.css'],
      head: [
        // Font Awesome 6 (free, solid) — matches the FOBO tool family's icon set.
        { tag: 'link', attrs: { rel: 'stylesheet', href: 'https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css' } },
        // Default to the light theme on first visit (user can still toggle).
        { tag: 'script', content: "try{if(!localStorage.getItem('starlight-theme')){localStorage.setItem('starlight-theme','light');document.documentElement.dataset.theme='light';}}catch(e){}" },
      ],
      social: {
        github: 'https://github.com/FOBO-Tools/fobo-data-maker',
      },
      sidebar: [
        { label: 'Start here', items: [
          { label: 'Introduction', link: '/' },
          { label: 'Getting started', link: '/getting-started/' },
        ] },
        { label: 'Concepts', items: [
          { label: 'The .dmf bundle', link: '/concepts/dmf/' },
          { label: 'Submission & encryption', link: '/concepts/submission/' },
          { label: 'Files & attachments', link: '/concepts/files/' },
        ] },
        { label: 'SDKs', items: [
          { label: 'JavaScript / Node', link: '/sdks/javascript/' },
          { label: 'Python', link: '/sdks/python/' },
          { label: '.NET', link: '/sdks/dotnet/' },
          { label: 'PHP', link: '/sdks/php/' },
        ] },
        { label: 'Renderers', items: [
          { label: 'Web embed', link: '/renderers/web-embed/' },
          { label: 'WordPress plugin', link: '/renderers/wordpress/' },
          { label: 'Terminal', link: '/renderers/terminal/' },
        ] },
        { label: 'Outegrations', items: [
          { label: 'Webhook', link: '/outegrations/webhook/' },
        ] },
        { label: 'Schema reference', items: [
          { label: 'Form model', link: '/schema/form-model/' },
          { label: 'Field kinds', link: '/schema/field-kinds/' },
          { label: 'Expressions & Fn', link: '/schema/expressions/' },
        ] },
        { label: 'Build your own', items: [
          { label: 'Custom client / renderer', link: '/build-your-own/' },
        ] },
      ],
    }),
  ],
});
