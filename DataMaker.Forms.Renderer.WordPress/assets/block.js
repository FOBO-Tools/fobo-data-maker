/**
 * Gutenberg block: DataMaker Form. Server-rendered — `edit` shows a
 * sidebar SelectControl listing every uploaded form, plus a placeholder
 * preview, while `save` returns null so the front-end calls the PHP
 * render_callback (which delegates to the shortcode).
 *
 * Vanilla `wp.element.createElement` (no JSX) so this ships without a
 * build step.
 */
(function (blocks, element, blockEditor, components, apiFetch, i18n) {
  'use strict';

  const el      = element.createElement;
  const useEffect = element.useEffect;
  const useState  = element.useState;
  const __      = i18n.__;

  blocks.registerBlockType('fobo/data-maker-form', {
    title:       __('FOBO Data Maker Form', 'fobo-data-maker-forms'),
    description: __('Render a form uploaded under FOBO Data Maker Forms → Upload .dmf.', 'fobo-data-maker-forms'),
    icon:        'feedback',
    category:    'embed',
    attributes:  {
      slug:  { type: 'string', default: '' },
      theme: { type: 'string', default: '' },  // '' = inherit per-form setting; 'on' / 'off' = override
    },
    supports:    { html: false, align: ['wide', 'full'] },

    edit: function (props) {
      const slug = props.attributes.slug || '';
      const [forms, setForms]   = useState(null);
      const [error, setError]   = useState(null);

      useEffect(function () {
        apiFetch({ path: '/fobo-data-maker/v1/forms' })
          .then(function (rows) { setForms(Array.isArray(rows) ? rows : []); })
          .catch(function (e)   { setError(e && e.message ? e.message : __('Could not load forms.', 'fobo-data-maker-forms')); });
      }, []);

      const options = [{ label: __('— Select a form —', 'fobo-data-maker-forms'), value: '' }];
      if (Array.isArray(forms)) {
        forms.forEach(function (f) { options.push({ label: f.label, value: f.slug }); });
      }

      const themeOptions = [
        { label: __('Inherit form setting',                'fobo-data-maker-forms'), value: ''    },
        { label: __('Always apply designer styling',       'fobo-data-maker-forms'), value: 'on'  },
        { label: __('Always strip designer styling',       'fobo-data-maker-forms'), value: 'off' },
      ];

      const sidebar = el(
        blockEditor.InspectorControls,
        {},
        el(components.PanelBody, { title: __('Form', 'fobo-data-maker-forms'), initialOpen: true },
          forms === null && !error
            ? el(components.Spinner)
            : error
              ? el(components.Notice, { status: 'error', isDismissible: false }, error)
              : el(components.SelectControl, {
                  label:    __('Uploaded form', 'fobo-data-maker-forms'),
                  value:    slug,
                  options:  options,
                  onChange: function (v) { props.setAttributes({ slug: v }); },
                  help:     __('Upload more forms under FOBO Data Maker Forms → Upload .dmf.', 'fobo-data-maker-forms'),
                }),
          el(components.SelectControl, {
            label:    __('Designer styling override', 'fobo-data-maker-forms'),
            value:    props.attributes.theme || '',
            options:  themeOptions,
            onChange: function (v) { props.setAttributes({ theme: v }); },
            help:     __('Layout always honors the form. This only flips colors / fonts / button styling from the Data Maker designer.', 'fobo-data-maker-forms'),
          }),
          el(components.ExternalLink, { href: '/wp-admin/admin.php?page=fobo-data-maker-forms' },
             __('Upload a .dmf', 'fobo-data-maker-forms'))
        )
      );

      const preview = el(
        'div',
        Object.assign({}, blockEditor.useBlockProps(), {
          style: {
            padding: '20px', border: '1px dashed #c3c4c7', borderRadius: '6px',
            background: '#fafafa', color: '#1d2327', textAlign: 'center',
          },
        }),
        el('strong', {}, __('FOBO Data Maker Form', 'fobo-data-maker-forms')),
        el('div', { style: { marginTop: '6px', fontSize: '13px', color: '#646970' } },
           slug
             ? __('Slug: ', 'fobo-data-maker-forms') + slug
             : __('Pick a form from the sidebar.', 'fobo-data-maker-forms'))
      );

      return el(element.Fragment, {}, sidebar, preview);
    },

    save: function () { return null; },  // server-rendered
  });
})(window.wp.blocks, window.wp.element, window.wp.blockEditor, window.wp.components, window.wp.apiFetch, window.wp.i18n);
