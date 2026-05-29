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
      title: 'FOBO Data Maker',
      description:
        'Build clients and renderers for Data Maker forms: submit records, render .dmf bundles, and read the form schema.',
      social: {
        github: 'https://github.com/ChivanCOM/fobo-data-maker', // TODO: confirm owner/org before publishing
      },
      sidebar: [
        { label: 'Start here', items: [
          { label: 'Introduction', link: '/' },
          { label: 'Getting started', link: '/getting-started/' },
        ] },
        { label: 'Concepts', items: [
          { label: 'The .dmf bundle', link: '/concepts/dmf/' },
          { label: 'Submission & encryption', link: '/concepts/submission/' },
        ] },
        { label: 'SDKs', items: [
          { label: 'JavaScript / Node', link: '/sdks/javascript/' },
          { label: 'Python', link: '/sdks/python/' },
          { label: '.NET', link: '/sdks/dotnet/' },
        ] },
        { label: 'Renderers', items: [
          { label: 'Web embed', link: '/renderers/web-embed/' },
          { label: 'WordPress plugin', link: '/renderers/wordpress/' },
          { label: 'Terminal', link: '/renderers/terminal/' },
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
