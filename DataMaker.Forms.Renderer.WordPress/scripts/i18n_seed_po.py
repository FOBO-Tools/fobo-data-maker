#!/usr/bin/env python3
"""
Seed languages/datamaker-renderer-<locale>.po from the extracted .pot.

Not a machine-translation pass — the strings below are hand-written
translations of the plugin's user-facing copy (admin UI + front-end
chrome + validation defaults + error states). Re-run after `make pot`
to fold newly-extracted strings into each .po: unknown msgids get an
empty msgstr (WordPress falls back to the English source), so a missing
translation degrades gracefully rather than showing a blank label.

    python3 scripts/i18n_seed_po.py
    make mo            # compile the .po files to .mo

Brand token "Data Maker" is left untranslated inside otherwise-translated
phrases. Product nouns ("Forms", "Settings", …) and all prose ARE
translated. File names use the full WordPress locale (fr_FR, es_ES,
de_DE, zh_CN) because WP resolves <textdomain>-<get_locale()>.mo.
"""
import os, re, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
POT  = os.path.join(HERE, '..', 'languages', 'datamaker-renderer.pot')

# WP locale code, Plural-Forms header, and the per-string table.
LOCALES = {
    'fr_FR': ('Français', 'nplurals=2; plural=(n > 1);'),
    'es_ES': ('Español',  'nplurals=2; plural=(n != 1);'),
    'de_DE': ('Deutsch',  'nplurals=2; plural=(n != 1);'),
    'zh_CN': ('简体中文',  'nplurals=1; plural=0;'),
}

# msgid -> {locale: translation}. Plural entries map to {locale: (one, other)}
# (zh_CN gives a single string). Anything absent = English fallback.
T = {
 "Data Maker Forms": {
   'fr_FR':"Formulaires Data Maker", 'es_ES':"Formularios Data Maker",
   'de_DE':"Data Maker Formulare", 'zh_CN':"Data Maker 表单"},
 "Renders Data Maker forms from signed .dmf uploads. POSTs sealed submissions to the Data Maker API; supports the localStorage-backed edit flow.": {
   'fr_FR':"Affiche les formulaires Data Maker à partir de fichiers .dmf signés. Envoie les soumissions scellées à l’API Data Maker ; prend en charge le flux de modification basé sur localStorage.",
   'es_ES':"Muestra formularios Data Maker a partir de archivos .dmf firmados. Envía los envíos sellados a la API de Data Maker; admite el flujo de edición basado en localStorage.",
   'de_DE':"Stellt Data-Maker-Formulare aus signierten .dmf-Uploads dar. Sendet versiegelte Übermittlungen an die Data-Maker-API; unterstützt den auf localStorage basierenden Bearbeitungsablauf.",
   'zh_CN':"从已签名的 .dmf 上传渲染 Data Maker 表单。将密封的提交内容发送到 Data Maker API；支持基于 localStorage 的编辑流程。"},
 "Form settings": {'fr_FR':"Réglages du formulaire", 'es_ES':"Ajustes del formulario", 'de_DE':"Formulareinstellungen", 'zh_CN':"表单设置"},
 "Missing or invalid form id.": {'fr_FR':"Identifiant de formulaire manquant ou invalide.", 'es_ES':"Falta el id del formulario o no es válido.", 'de_DE':"Formular-ID fehlt oder ist ungültig.", 'zh_CN':"缺少表单 id 或无效。"},
 "Form not found.": {'fr_FR':"Formulaire introuvable.", 'es_ES':"Formulario no encontrado.", 'de_DE':"Formular nicht gefunden.", 'zh_CN':"未找到表单。"},
 "Saved.": {'fr_FR':"Enregistré.", 'es_ES':"Guardado.", 'de_DE':"Gespeichert.", 'zh_CN':"已保存。"},
 "Form settings — %s": {'fr_FR':"Réglages du formulaire — %s", 'es_ES':"Ajustes del formulario — %s", 'de_DE':"Formulareinstellungen — %s", 'zh_CN':"表单设置 — %s"},
 "Back to forms": {'fr_FR':"Retour aux formulaires", 'es_ES':"Volver a los formularios", 'de_DE':"Zurück zu den Formularen", 'zh_CN':"返回表单列表"},
 "Behaviour": {'fr_FR':"Comportement", 'es_ES':"Comportamiento", 'de_DE':"Verhalten", 'zh_CN':"行为"},
 "Designer styling": {'fr_FR':"Style du concepteur", 'es_ES':"Estilo del diseñador", 'de_DE':"Designer-Styling", 'zh_CN':"设计器样式"},
 "Apply Theme/Styling": {'fr_FR':"Appliquer le thème / le style", 'es_ES':"Aplicar tema/estilo", 'de_DE':"Theme/Styling anwenden", 'zh_CN':"应用主题/样式"},
 "On = render the form the way it looks in the desktop designer (palette, fonts, button variants, heading styles, per-element overrides). Off = strip all of that and let the active WordPress theme drive the look. Layout (rows, columns, spacing) is honored in both modes.": {
   'fr_FR':"Activé = affiche le formulaire tel qu’il apparaît dans le concepteur de bureau (palette, polices, variantes de boutons, styles de titres, surcharges par élément). Désactivé = supprime tout cela et laisse le thème WordPress actif définir l’apparence. La mise en page (lignes, colonnes, espacement) est respectée dans les deux modes.",
   'es_ES':"Activado = muestra el formulario tal como se ve en el diseñador de escritorio (paleta, fuentes, variantes de botones, estilos de encabezado, anulaciones por elemento). Desactivado = elimina todo eso y deja que el tema de WordPress activo defina el aspecto. El diseño (filas, columnas, espaciado) se respeta en ambos modos.",
   'de_DE':"Ein = stellt das Formular so dar, wie es im Desktop-Designer aussieht (Palette, Schriften, Button-Varianten, Überschriftenstile, Überschreibungen pro Element). Aus = entfernt all das und überlässt das Aussehen dem aktiven WordPress-Theme. Das Layout (Zeilen, Spalten, Abstände) wird in beiden Modi beibehalten.",
   'zh_CN':"开启 = 按桌面设计器中的样子渲染表单（调色板、字体、按钮变体、标题样式、按元素覆盖）。关闭 = 去除全部这些，由当前 WordPress 主题决定外观。两种模式下都保留布局（行、列、间距）。"},
 "Edit flow": {'fr_FR':"Flux de modification", 'es_ES':"Flujo de edición", 'de_DE':"Bearbeitungsablauf", 'zh_CN':"编辑流程"},
 "Let submitters edit their submission later (browser localStorage)": {
   'fr_FR':"Permettre aux participants de modifier leur soumission plus tard (localStorage du navigateur)",
   'es_ES':"Permitir que quienes envían editen su envío más tarde (localStorage del navegador)",
   'de_DE':"Einsendern erlauben, ihre Übermittlung später zu bearbeiten (Browser-localStorage)",
   'zh_CN':"允许提交者稍后编辑其提交（浏览器 localStorage）"},
 "When on, a submitter returning to the same form on the same browser sees \"Continue editing?\" before a fresh start.": {
   'fr_FR':"Lorsqu’activé, un participant revenant sur le même formulaire dans le même navigateur voit « Continuer la modification ? » avant de recommencer.",
   'es_ES':"Cuando está activado, quien vuelve al mismo formulario en el mismo navegador ve «¿Continuar editando?» antes de empezar de nuevo.",
   'de_DE':"Wenn aktiviert, sieht ein Einsender, der dasselbe Formular im selben Browser erneut aufruft, „Bearbeitung fortsetzen?“, bevor er neu beginnt.",
   'zh_CN':"开启时，在同一浏览器中再次打开同一表单的提交者会先看到“继续编辑？”，然后才重新开始。"},
 "After submit": {'fr_FR':"Après l’envoi", 'es_ES':"Después de enviar", 'de_DE':"Nach dem Absenden", 'zh_CN':"提交后"},
 "Stay on page (default)": {'fr_FR':"Rester sur la page (par défaut)", 'es_ES':"Permanecer en la página (predeterminado)", 'de_DE':"Auf der Seite bleiben (Standard)", 'zh_CN':"停留在页面（默认）"},
 "Redirect to a WordPress page:": {'fr_FR':"Rediriger vers une page WordPress :", 'es_ES':"Redirigir a una página de WordPress:", 'de_DE':"Auf eine WordPress-Seite weiterleiten:", 'zh_CN':"重定向到 WordPress 页面："},
 "select a page": {'fr_FR':"sélectionner une page", 'es_ES':"seleccionar una página", 'de_DE':"Seite auswählen", 'zh_CN':"选择页面"},
 "Redirect to a URL:": {'fr_FR':"Rediriger vers une URL :", 'es_ES':"Redirigir a una URL:", 'de_DE':"Auf eine URL weiterleiten:", 'zh_CN':"重定向到 URL："},
 "Browser navigates to the chosen target after a successful submission. \"Stay on page\" replaces the form with the success message below.": {
   'fr_FR':"Le navigateur accède à la cible choisie après un envoi réussi. « Rester sur la page » remplace le formulaire par le message de réussite ci-dessous.",
   'es_ES':"El navegador va al destino elegido tras un envío correcto. «Permanecer en la página» reemplaza el formulario por el mensaje de éxito de abajo.",
   'de_DE':"Der Browser navigiert nach einer erfolgreichen Übermittlung zum gewählten Ziel. „Auf der Seite bleiben“ ersetzt das Formular durch die untenstehende Erfolgsmeldung.",
   'zh_CN':"提交成功后浏览器会跳转到所选目标。“停留在页面”会用下方的成功消息替换表单。"},
 "Success message": {'fr_FR':"Message de réussite", 'es_ES':"Mensaje de éxito", 'de_DE':"Erfolgsmeldung", 'zh_CN':"成功消息"},
 "## Thanks for your submission.": {'fr_FR':"## Merci pour votre envoi.", 'es_ES':"## Gracias por su envío.", 'de_DE':"## Danke für Ihre Übermittlung.", 'zh_CN':"## 感谢您的提交。"},
 "Markdown allowed (%1$s, %2$s, %3$s, %4$s, %5$s). Rendered inside the form's container after a successful submit when \"After submit\" is set to %6$s; ignored for redirect modes. Leave blank for the default.": {
   'fr_FR':"Markdown autorisé (%1$s, %2$s, %3$s, %4$s, %5$s). Affiché dans le conteneur du formulaire après un envoi réussi lorsque « Après l’envoi » est réglé sur %6$s ; ignoré pour les modes de redirection. Laissez vide pour la valeur par défaut.",
   'es_ES':"Markdown permitido (%1$s, %2$s, %3$s, %4$s, %5$s). Se muestra dentro del contenedor del formulario tras un envío correcto cuando «Después de enviar» es %6$s; se ignora en los modos de redirección. Déjelo en blanco para el valor predeterminado.",
   'de_DE':"Markdown erlaubt (%1$s, %2$s, %3$s, %4$s, %5$s). Wird nach einer erfolgreichen Übermittlung im Formularcontainer angezeigt, wenn „Nach dem Absenden“ auf %6$s gesetzt ist; bei Weiterleitungsmodi ignoriert. Für den Standard leer lassen.",
   'zh_CN':"允许使用 Markdown（%1$s、%2$s、%3$s、%4$s、%5$s）。当“提交后”设置为 %6$s 时，提交成功后会在表单容器内渲染；重定向模式下忽略。留空则使用默认值。"},
 "Stay on page": {'fr_FR':"Rester sur la page", 'es_ES':"Permanecer en la página", 'de_DE':"Auf der Seite bleiben", 'zh_CN':"停留在页面"},
 "Visibility": {'fr_FR':"Visibilité", 'es_ES':"Visibilidad", 'de_DE':"Sichtbarkeit", 'zh_CN':"可见性"},
 "Uncheck any item to hide it from this form on the WordPress site. Filtering happens server-side, before the renderer reads the form, so the layout grid auto-flows around the gaps.": {
   'fr_FR':"Décochez un élément pour le masquer de ce formulaire sur le site WordPress. Le filtrage a lieu côté serveur, avant que le moteur de rendu ne lise le formulaire ; la grille de mise en page se réorganise donc automatiquement autour des vides.",
   'es_ES':"Desmarque cualquier elemento para ocultarlo de este formulario en el sitio WordPress. El filtrado se hace en el servidor, antes de que el renderizador lea el formulario, por lo que la cuadrícula del diseño se reorganiza automáticamente alrededor de los huecos.",
   'de_DE':"Deaktivieren Sie ein Element, um es in diesem Formular auf der WordPress-Website auszublenden. Die Filterung erfolgt serverseitig, bevor der Renderer das Formular liest, sodass das Layout-Raster automatisch um die Lücken herum umfließt.",
   'zh_CN':"取消勾选任意项即可在 WordPress 网站上将其从此表单中隐藏。过滤在服务器端进行，在渲染器读取表单之前完成，因此布局网格会自动围绕空缺重新排列。"},
 "Show or hide every non-required item at once": {'fr_FR':"Afficher ou masquer tous les éléments non obligatoires en une fois", 'es_ES':"Mostrar u ocultar todos los elementos no obligatorios a la vez", 'de_DE':"Alle nicht erforderlichen Elemente auf einmal ein- oder ausblenden", 'zh_CN':"一次显示或隐藏所有非必填项"},
 "all": {'fr_FR':"tout", 'es_ES':"todo", 'de_DE':"alle", 'zh_CN':"全部"},
 "Item": {'fr_FR':"Élément", 'es_ES':"Elemento", 'de_DE':"Element", 'zh_CN':"项目"},
 "Kind": {'fr_FR':"Type", 'es_ES':"Tipo", 'de_DE':"Art", 'zh_CN':"种类"},
 "Where": {'fr_FR':"Emplacement", 'es_ES':"Ubicación", 'de_DE':"Wo", 'zh_CN':"位置"},
 "No layout elements found.": {'fr_FR':"Aucun élément de mise en page trouvé.", 'es_ES':"No se encontraron elementos de diseño.", 'de_DE':"Keine Layout-Elemente gefunden.", 'zh_CN':"未找到布局元素。"},
 "Required fields can't be hidden": {'fr_FR':"Les champs obligatoires ne peuvent pas être masqués", 'es_ES':"Los campos obligatorios no se pueden ocultar", 'de_DE':"Pflichtfelder können nicht ausgeblendet werden", 'zh_CN':"必填字段无法隐藏"},
 "required": {'fr_FR':"obligatoire", 'es_ES':"obligatorio", 'de_DE':"erforderlich", 'zh_CN':"必填"},
 "A checked box = %1$s. The form posts the inverted set as %2$s so legacy fields not yet in the form definition aren't accidentally hidden when added later.": {
   'fr_FR':"Une case cochée = %1$s. Le formulaire envoie l’ensemble inversé sous forme de %2$s afin que les anciens champs pas encore présents dans la définition ne soient pas masqués par accident lorsqu’ils sont ajoutés ultérieurement.",
   'es_ES':"Una casilla marcada = %1$s. El formulario envía el conjunto invertido como %2$s para que los campos heredados que aún no están en la definición no se oculten por accidente al añadirse más tarde.",
   'de_DE':"Ein angekreuztes Kästchen = %1$s. Das Formular sendet die umgekehrte Menge als %2$s, damit ältere Felder, die noch nicht in der Formulardefinition enthalten sind, beim späteren Hinzufügen nicht versehentlich ausgeblendet werden.",
   'zh_CN':"勾选的复选框 = %1$s。表单以 %2$s 提交反向集合，这样以后新增的、尚未在表单定义中的旧字段不会被意外隐藏。"},
 "visible": {'fr_FR':"visible", 'es_ES':"visible", 'de_DE':"sichtbar", 'zh_CN':"可见"},
 "Privacy &amp; consent": {'fr_FR':"Confidentialité et consentement", 'es_ES':"Privacidad y consentimiento", 'de_DE':"Datenschutz &amp; Einwilligung", 'zh_CN':"隐私与同意"},
 "Privacy policy target": {'fr_FR':"Cible de la politique de confidentialité", 'es_ES':"Destino de la política de privacidad", 'de_DE':"Ziel der Datenschutzerklärung", 'zh_CN':"隐私政策目标"},
 "Not set": {'fr_FR':"Non défini", 'es_ES':"Sin definir", 'de_DE':"Nicht festgelegt", 'zh_CN':"未设置"},
 "WordPress page:": {'fr_FR':"Page WordPress :", 'es_ES':"Página de WordPress:", 'de_DE':"WordPress-Seite:", 'zh_CN':"WordPress 页面："},
 "External URL:": {'fr_FR':"URL externe :", 'es_ES':"URL externa:", 'de_DE':"Externe URL:", 'zh_CN':"外部 URL："},
 "Linked from the consent label below when set.": {'fr_FR':"Lié depuis l’étiquette de consentement ci-dessous lorsqu’elle est définie.", 'es_ES':"Se enlaza desde la etiqueta de consentimiento de abajo cuando está definida.", 'de_DE':"Wird, sofern festgelegt, aus dem untenstehenden Einwilligungstext verlinkt.", 'zh_CN':"设置后，将从下方的同意标签处链接。"},
 "Privacy link text": {'fr_FR':"Texte du lien de confidentialité", 'es_ES':"Texto del enlace de privacidad", 'de_DE':"Linktext für den Datenschutz", 'zh_CN':"隐私链接文字"},
 "privacy policy": {'fr_FR':"politique de confidentialité", 'es_ES':"política de privacidad", 'de_DE':"Datenschutzerklärung", 'zh_CN':"隐私政策"},
 "Text shown as the link to the privacy target. Empty = \"privacy policy\".": {
   'fr_FR':"Texte affiché comme lien vers la cible de confidentialité. Vide = « politique de confidentialité ».",
   'es_ES':"Texto mostrado como enlace al destino de privacidad. Vacío = «política de privacidad».",
   'de_DE':"Text, der als Link zum Datenschutzziel angezeigt wird. Leer = „Datenschutzerklärung“.",
   'zh_CN':"显示为隐私目标链接的文字。留空 = “隐私政策”。"},
 "Require consent before submit": {'fr_FR':"Exiger le consentement avant l’envoi", 'es_ES':"Requerir consentimiento antes de enviar", 'de_DE':"Einwilligung vor dem Absenden verlangen", 'zh_CN':"提交前需要同意"},
 "Show a consent checkbox above the submit button; block POST until ticked.": {
   'fr_FR':"Afficher une case de consentement au-dessus du bouton d’envoi ; bloquer l’envoi tant qu’elle n’est pas cochée.",
   'es_ES':"Mostrar una casilla de consentimiento encima del botón de envío; bloquear el envío hasta que se marque.",
   'de_DE':"Ein Einwilligungskästchen über der Senden-Schaltfläche anzeigen; das Absenden blockieren, bis es angekreuzt ist.",
   'zh_CN':"在提交按钮上方显示同意复选框；在勾选前阻止提交。"},
 "I agree to the privacy policy.": {'fr_FR':"J’accepte la politique de confidentialité.", 'es_ES':"Acepto la política de privacidad.", 'de_DE':"Ich stimme der Datenschutzerklärung zu.", 'zh_CN':"我同意隐私政策。"},
 "Use {privacy} as a placeholder to insert a link to the URL above.": {
   'fr_FR':"Utilisez {privacy} comme marqueur pour insérer un lien vers l’URL ci-dessus.",
   'es_ES':"Use {privacy} como marcador para insertar un enlace a la URL de arriba.",
   'de_DE':"Verwenden Sie {privacy} als Platzhalter, um einen Link zur obigen URL einzufügen.",
   'zh_CN':"使用 {privacy} 作为占位符，以插入指向上方 URL 的链接。"},
 "Anti-abuse": {'fr_FR':"Anti-abus", 'es_ES':"Antiabuso", 'de_DE':"Missbrauchsschutz", 'zh_CN':"防滥用"},
 "Honeypot field": {'fr_FR':"Champ pot de miel", 'es_ES':"Campo trampa (honeypot)", 'de_DE':"Honeypot-Feld", 'zh_CN':"蜜罐字段"},
 "Render a hidden field; reject submissions that fill it. Recommended.": {
   'fr_FR':"Afficher un champ caché ; rejeter les soumissions qui le remplissent. Recommandé.",
   'es_ES':"Mostrar un campo oculto; rechazar los envíos que lo rellenen. Recomendado.",
   'de_DE':"Ein verstecktes Feld einfügen; Übermittlungen ablehnen, die es ausfüllen. Empfohlen.",
   'zh_CN':"渲染一个隐藏字段；拒绝填写了该字段的提交。建议启用。"},
 "Rate limit (per minute, per IP)": {'fr_FR':"Limite de débit (par minute, par IP)", 'es_ES':"Límite de tasa (por minuto, por IP)", 'de_DE':"Ratenbegrenzung (pro Minute, pro IP)", 'zh_CN':"速率限制（每分钟，每个 IP）"},
 "0 = no limit (a plugin-wide default still applies). Soft throttle enforced via WP transients.": {
   'fr_FR':"0 = aucune limite (une valeur par défaut globale au plugin s’applique tout de même). Limitation souple appliquée via les transients WP.",
   'es_ES':"0 = sin límite (se sigue aplicando un valor predeterminado de todo el plugin). Limitación flexible aplicada mediante transients de WP.",
   'de_DE':"0 = kein Limit (ein pluginweiter Standard gilt dennoch). Sanfte Drosselung über WP-Transients durchgesetzt.",
   'zh_CN':"0 = 无限制（仍会应用插件级别的默认值）。通过 WP transient 实施软性限流。"},
 "Cloudflare Turnstile": {'fr_FR':"Cloudflare Turnstile", 'es_ES':"Cloudflare Turnstile", 'de_DE':"Cloudflare Turnstile", 'zh_CN':"Cloudflare Turnstile"},
 "Require visitors to pass a Turnstile challenge before submitting.": {
   'fr_FR':"Exiger des visiteurs qu’ils réussissent un défi Turnstile avant d’envoyer.",
   'es_ES':"Exigir a los visitantes que superen un desafío Turnstile antes de enviar.",
   'de_DE':"Von Besuchern verlangen, vor dem Absenden eine Turnstile-Aufgabe zu bestehen.",
   'zh_CN':"要求访问者在提交前通过 Turnstile 验证。"},
 "Turnstile keys not configured. Set them in %s first.": {
   'fr_FR':"Clés Turnstile non configurées. Définissez-les d’abord dans %s.",
   'es_ES':"Claves de Turnstile no configuradas. Defínalas primero en %s.",
   'de_DE':"Turnstile-Schlüssel nicht konfiguriert. Legen Sie sie zuerst unter %s fest.",
   'zh_CN':"未配置 Turnstile 密钥。请先在 %s 中设置。"},
 "Data Maker Forms → Settings": {'fr_FR':"Formulaires Data Maker → Réglages", 'es_ES':"Formularios Data Maker → Ajustes", 'de_DE':"Data Maker Formulare → Einstellungen", 'zh_CN':"Data Maker 表单 → 设置"},
 "Server verifies each token with Cloudflare before sealing the submission. Honeypot + rate limit still apply.": {
   'fr_FR':"Le serveur vérifie chaque jeton auprès de Cloudflare avant de sceller la soumission. Le pot de miel et la limite de débit s’appliquent toujours.",
   'es_ES':"El servidor verifica cada token con Cloudflare antes de sellar el envío. El honeypot y el límite de tasa siguen aplicándose.",
   'de_DE':"Der Server überprüft jedes Token bei Cloudflare, bevor die Übermittlung versiegelt wird. Honeypot und Ratenbegrenzung gelten weiterhin.",
   'zh_CN':"服务器在密封提交前会通过 Cloudflare 验证每个令牌。蜜罐和速率限制仍然有效。"},
 "Integrations": {'fr_FR':"Intégrations", 'es_ES':"Integraciones", 'de_DE':"Integrationen", 'zh_CN':"集成"},
 "Webhook URL": {'fr_FR':"URL du webhook", 'es_ES':"URL del webhook", 'de_DE':"Webhook-URL", 'zh_CN':"Webhook URL"},
 "POSTed with submission metadata (no plaintext field values) after a successful sealed submit. For full notification flows use the WP action hook %s — works with Post SMTP, Mailpoet, WPForms add-ons, Zapier, custom code.": {
   'fr_FR':"Envoyé en POST avec les métadonnées de la soumission (aucune valeur de champ en clair) après un envoi scellé réussi. Pour des flux de notification complets, utilisez le hook d’action WP %s — compatible avec Post SMTP, Mailpoet, les extensions WPForms, Zapier et du code personnalisé.",
   'es_ES':"Se envía por POST con los metadatos del envío (sin valores de campo en texto plano) tras un envío sellado correcto. Para flujos de notificación completos use el hook de acción de WP %s — funciona con Post SMTP, Mailpoet, complementos de WPForms, Zapier y código personalizado.",
   'de_DE':"Wird nach einer erfolgreichen versiegelten Übermittlung mit den Übermittlungs-Metadaten (keine Feldwerte im Klartext) per POST gesendet. Für vollständige Benachrichtigungsabläufe verwenden Sie den WP-Action-Hook %s — funktioniert mit Post SMTP, Mailpoet, WPForms-Erweiterungen, Zapier und eigenem Code.",
   'zh_CN':"在密封提交成功后，以 POST 方式发送提交元数据（不含明文字段值）。如需完整的通知流程，请使用 WP 动作钩子 %s — 可配合 Post SMTP、Mailpoet、WPForms 附加组件、Zapier 及自定义代码。"},
 "Form-wide messages": {'fr_FR':"Messages globaux du formulaire", 'es_ES':"Mensajes de todo el formulario", 'de_DE':"Formularweite Meldungen", 'zh_CN':"表单级消息"},
 "Form-level text the renderer shows, independent of any single field. Empty box = use the form's default (set in the designer) or, failing that, the engine's English fallback.": {
   'fr_FR':"Texte au niveau du formulaire affiché par le moteur de rendu, indépendamment de tout champ. Champ vide = utiliser la valeur par défaut du formulaire (définie dans le concepteur) ou, à défaut, la valeur de repli anglaise du moteur.",
   'es_ES':"Texto a nivel de formulario que muestra el renderizador, independiente de cualquier campo. Caja vacía = usar el valor predeterminado del formulario (definido en el diseñador) o, en su defecto, la reserva en inglés del motor.",
   'de_DE':"Text auf Formularebene, den der Renderer anzeigt, unabhängig von einzelnen Feldern. Leeres Feld = Standard des Formulars verwenden (im Designer festgelegt) oder andernfalls den englischen Rückfallwert der Engine.",
   'zh_CN':"渲染器显示的表单级文本，与任何单个字段无关。留空 = 使用表单的默认值（在设计器中设置），否则使用引擎的英文回退值。"},
 "Field error messages": {'fr_FR':"Messages d’erreur de champ", 'es_ES':"Mensajes de error de campo", 'de_DE':"Feld-Fehlermeldungen", 'zh_CN':"字段错误消息"},
 "Override the validation error text shown next to each field. Empty box = use the form's default (set in the designer) or, failing that, the engine's English fallback. Both are shown as placeholder text inside each box.": {
   'fr_FR':"Remplacez le texte d’erreur de validation affiché à côté de chaque champ. Champ vide = utiliser la valeur par défaut du formulaire (définie dans le concepteur) ou, à défaut, la valeur de repli anglaise du moteur. Les deux apparaissent comme texte indicatif dans chaque champ.",
   'es_ES':"Anule el texto de error de validación que se muestra junto a cada campo. Caja vacía = usar el valor predeterminado del formulario (definido en el diseñador) o, en su defecto, la reserva en inglés del motor. Ambos se muestran como texto de marcador dentro de cada caja.",
   'de_DE':"Überschreiben Sie den Validierungs-Fehlertext, der neben jedem Feld angezeigt wird. Leeres Feld = Standard des Formulars verwenden (im Designer festgelegt) oder andernfalls den englischen Rückfallwert der Engine. Beide werden als Platzhaltertext in jedem Feld angezeigt.",
   'zh_CN':"覆盖每个字段旁显示的验证错误文本。留空 = 使用表单的默认值（在设计器中设置），否则使用引擎的英文回退值。两者都会作为占位文本显示在各输入框内。"},
 "No fields in this form expose customizable message slots. Toggle Required, set field options (min/max length, allowed extensions, etc.) in the designer to enable per-check overrides.": {
   'fr_FR':"Aucun champ de ce formulaire n’expose de messages personnalisables. Activez « Obligatoire » ou définissez des options de champ (longueur min/max, extensions autorisées, etc.) dans le concepteur pour activer les surcharges par contrôle.",
   'es_ES':"Ningún campo de este formulario expone mensajes personalizables. Active «Obligatorio» o configure opciones de campo (longitud mín./máx., extensiones permitidas, etc.) en el diseñador para habilitar las anulaciones por comprobación.",
   'de_DE':"Kein Feld in diesem Formular stellt anpassbare Meldungs-Slots bereit. Aktivieren Sie „Erforderlich“ oder legen Sie Feldoptionen (Min./Max.-Länge, erlaubte Erweiterungen usw.) im Designer fest, um Überschreibungen pro Prüfung zu ermöglichen.",
   'zh_CN':"此表单中没有字段暴露可自定义的消息槽。请在设计器中切换“必填”、设置字段选项（最小/最大长度、允许的扩展名等），以启用按校验项的覆盖。"},
 "Save form settings": {'fr_FR':"Enregistrer les réglages du formulaire", 'es_ES':"Guardar los ajustes del formulario", 'de_DE':"Formulareinstellungen speichern", 'zh_CN':"保存表单设置"},
 "Step %d": {'fr_FR':"Étape %d", 'es_ES':"Paso %d", 'de_DE':"Schritt %d", 'zh_CN':"第 %d 步"},
 "Section %d": {'fr_FR':"Section %d", 'es_ES':"Sección %d", 'de_DE':"Abschnitt %d", 'zh_CN':"第 %d 节"},
 "(group)": {'fr_FR':"(groupe)", 'es_ES':"(grupo)", 'de_DE':"(Gruppe)", 'zh_CN':"（组）"},
 "(heading)": {'fr_FR':"(titre)", 'es_ES':"(encabezado)", 'de_DE':"(Überschrift)", 'zh_CN':"（标题）"},
 "(rich text)": {'fr_FR':"(texte enrichi)", 'es_ES':"(texto enriquecido)", 'de_DE':"(Rich-Text)", 'zh_CN':"（富文本）"},
 "(image)": {'fr_FR':"(image)", 'es_ES':"(imagen)", 'de_DE':"(Bild)", 'zh_CN':"（图片）"},
 "(divider)": {'fr_FR':"(séparateur)", 'es_ES':"(separador)", 'de_DE':"(Trennlinie)", 'zh_CN':"（分隔线）"},
 "(spacer)": {'fr_FR':"(espace)", 'es_ES':"(espaciador)", 'de_DE':"(Abstand)", 'zh_CN':"（间隔）"},
 "(button)": {'fr_FR':"(bouton)", 'es_ES':"(botón)", 'de_DE':"(Schaltfläche)", 'zh_CN':"（按钮）"},
 "Form deleted.": {'fr_FR':"Formulaire supprimé.", 'es_ES':"Formulario eliminado.", 'de_DE':"Formular gelöscht.", 'zh_CN':"表单已删除。"},
 "Forbidden — nonce check failed.": {'fr_FR':"Interdit — échec de la vérification du nonce.", 'es_ES':"Prohibido — falló la comprobación del nonce.", 'de_DE':"Verboten — Nonce-Prüfung fehlgeschlagen.", 'zh_CN':"禁止访问 — nonce 校验失败。"},
 "Upload .dmf": {'fr_FR':"Téléverser un .dmf", 'es_ES':"Subir .dmf", 'de_DE':".dmf hochladen", 'zh_CN':"上传 .dmf"},
 "Slug (shortcode id)": {'fr_FR':"Slug (id du shortcode)", 'es_ES':"Slug (id del shortcode)", 'de_DE':"Slug (Shortcode-ID)", 'zh_CN':"Slug（短代码 id）"},
 "Form id": {'fr_FR':"Id du formulaire", 'es_ES':"Id del formulario", 'de_DE':"Formular-ID", 'zh_CN':"表单 id"},
 "Schema": {'fr_FR':"Schéma", 'es_ES':"Esquema", 'de_DE':"Schema", 'zh_CN':"架构"},
 "Signed": {'fr_FR':"Signé", 'es_ES':"Firmado", 'de_DE':"Signiert", 'zh_CN':"已签名"},
 "Uploaded": {'fr_FR':"Téléversé", 'es_ES':"Subido", 'de_DE':"Hochgeladen", 'zh_CN':"已上传"},
 "No forms uploaded yet.": {'fr_FR':"Aucun formulaire téléversé pour l’instant.", 'es_ES':"Aún no se ha subido ningún formulario.", 'de_DE':"Noch keine Formulare hochgeladen.", 'zh_CN':"尚未上传任何表单。"},
 "yes": {'fr_FR':"oui", 'es_ES':"sí", 'de_DE':"ja", 'zh_CN':"是"},
 "no": {'fr_FR':"non", 'es_ES':"no", 'de_DE':"nein", 'zh_CN':"否"},
 "Preview": {'fr_FR':"Aperçu", 'es_ES':"Vista previa", 'de_DE':"Vorschau", 'zh_CN':"预览"},
 "Settings": {'fr_FR':"Réglages", 'es_ES':"Ajustes", 'de_DE':"Einstellungen", 'zh_CN':"设置"},
 "Delete this form?": {'fr_FR':"Supprimer ce formulaire ?", 'es_ES':"¿Eliminar este formulario?", 'de_DE':"Dieses Formular löschen?", 'zh_CN':"删除此表单？"},
 "Delete": {'fr_FR':"Supprimer", 'es_ES':"Eliminar", 'de_DE':"Löschen", 'zh_CN':"删除"},
 "Preview form": {'fr_FR':"Aperçu du formulaire", 'es_ES':"Vista previa del formulario", 'de_DE':"Formularvorschau", 'zh_CN':"预览表单"},
 "Preview — %s": {'fr_FR':"Aperçu — %s", 'es_ES':"Vista previa — %s", 'de_DE':"Vorschau — %s", 'zh_CN':"预览 — %s"},
 "Form preview": {'fr_FR':"Aperçu du formulaire", 'es_ES':"Vista previa del formulario", 'de_DE':"Formularvorschau", 'zh_CN':"表单预览"},
 "Form preview — %s": {'fr_FR':"Aperçu du formulaire — %s", 'es_ES':"Vista previa del formulario — %s", 'de_DE':"Formularvorschau — %s", 'zh_CN':"表单预览 — %s"},
 "Open in new tab": {'fr_FR':"Ouvrir dans un nouvel onglet", 'es_ES':"Abrir en una nueva pestaña", 'de_DE':"In neuem Tab öffnen", 'zh_CN':"在新标签页中打开"},
 "Shortcode: %s — paste this anywhere on the site to embed the form.": {
   'fr_FR':"Shortcode : %s — collez-le n’importe où sur le site pour intégrer le formulaire.",
   'es_ES':"Shortcode: %s — péguelo en cualquier parte del sitio para insertar el formulario.",
   'de_DE':"Shortcode: %s — fügen Sie ihn an beliebiger Stelle der Website ein, um das Formular einzubetten.",
   'zh_CN':"短代码：%s — 将其粘贴到网站任意位置即可嵌入表单。"},
 "Preview submits go through the same pipeline as the live site (sealed POST → API). Use a test recipient inbox before sharing the form publicly.": {
   'fr_FR':"Les envois d’aperçu suivent le même pipeline que le site en production (POST scellé → API). Utilisez une boîte de réception destinataire de test avant de partager le formulaire publiquement.",
   'es_ES':"Los envíos de la vista previa pasan por el mismo flujo que el sitio en producción (POST sellado → API). Use una bandeja de destinatario de prueba antes de compartir el formulario públicamente.",
   'de_DE':"Vorschau-Übermittlungen durchlaufen dieselbe Pipeline wie die Live-Website (versiegelter POST → API). Verwenden Sie ein Test-Empfängerpostfach, bevor Sie das Formular öffentlich teilen.",
   'zh_CN':"预览提交与正式网站走相同的流程（密封 POST → API）。在公开分享表单前，请使用测试收件箱。"},
 "Data Maker Forms — Settings": {'fr_FR':"Formulaires Data Maker — Réglages", 'es_ES':"Formularios Data Maker — Ajustes", 'de_DE':"Data Maker Formulare — Einstellungen", 'zh_CN':"Data Maker 表单 — 设置"},
 "Signature verification": {'fr_FR':"Vérification de signature", 'es_ES':"Verificación de firma", 'de_DE':"Signaturprüfung", 'zh_CN':"签名验证"},
 "Require uploaded .dmf bundles to be Ed25519-signed": {'fr_FR':"Exiger que les fichiers .dmf téléversés soient signés en Ed25519", 'es_ES':"Exigir que los paquetes .dmf subidos estén firmados con Ed25519", 'de_DE':"Hochgeladene .dmf-Bundles müssen Ed25519-signiert sein", 'zh_CN':"要求上传的 .dmf 包使用 Ed25519 签名"},
 "base64-encoded signer pubkey (optional)": {'fr_FR':"clé publique du signataire encodée en base64 (facultatif)", 'es_ES':"clave pública del firmante codificada en base64 (opcional)", 'de_DE':"Base64-codierter öffentlicher Signaturschlüssel (optional)", 'zh_CN':"base64 编码的签名者公钥（可选）"},
 "If set, the uploaded .dmf must be signed with exactly this pubkey. Leave blank to accept any signed bundle.": {
   'fr_FR':"Si défini, le .dmf téléversé doit être signé avec exactement cette clé publique. Laissez vide pour accepter tout fichier signé.",
   'es_ES':"Si se define, el .dmf subido debe estar firmado exactamente con esta clave pública. Déjelo en blanco para aceptar cualquier paquete firmado.",
   'de_DE':"Wenn festgelegt, muss die hochgeladene .dmf genau mit diesem öffentlichen Schlüssel signiert sein. Leer lassen, um jedes signierte Bundle zu akzeptieren.",
   'zh_CN':"如果设置，上传的 .dmf 必须正好使用此公钥签名。留空则接受任何已签名的包。"},
 "Privacy-friendly CAPTCHA challenge. Enroll for free at %s; paste the site & secret keys here. Each form chooses whether to require it via Form Settings.": {
   'fr_FR':"Défi CAPTCHA respectueux de la vie privée. Inscrivez-vous gratuitement sur %s ; collez ici les clés de site et secrète. Chaque formulaire choisit de l’exiger ou non via ses réglages.",
   'es_ES':"Desafío CAPTCHA respetuoso con la privacidad. Regístrese gratis en %s; pegue aquí las claves de sitio y secreta. Cada formulario decide si lo requiere desde sus ajustes.",
   'de_DE':"Datenschutzfreundliche CAPTCHA-Aufgabe. Melden Sie sich kostenlos bei %s an; fügen Sie hier den Site- und den geheimen Schlüssel ein. Jedes Formular entscheidet über seine Einstellungen, ob es erforderlich ist.",
   'zh_CN':"注重隐私的 CAPTCHA 验证。可在 %s 免费注册；将站点密钥和密钥粘贴到此处。每个表单可在表单设置中选择是否要求它。"},
 "Site key": {'fr_FR':"Clé de site", 'es_ES':"Clave de sitio", 'de_DE':"Site-Schlüssel", 'zh_CN':"站点密钥"},
 "Public key embedded in the form page (data-sitekey).": {'fr_FR':"Clé publique intégrée dans la page du formulaire (data-sitekey).", 'es_ES':"Clave pública incrustada en la página del formulario (data-sitekey).", 'de_DE':"Öffentlicher Schlüssel, der in die Formularseite eingebettet wird (data-sitekey).", 'zh_CN':"嵌入表单页面的公钥（data-sitekey）。"},
 "Secret key": {'fr_FR':"Clé secrète", 'es_ES':"Clave secreta", 'de_DE':"Geheimer Schlüssel", 'zh_CN':"密钥"},
 "Used server-side to verify tokens with Cloudflare. Never sent to the browser.": {
   'fr_FR':"Utilisée côté serveur pour vérifier les jetons auprès de Cloudflare. Jamais envoyée au navigateur.",
   'es_ES':"Se usa en el servidor para verificar tokens con Cloudflare. Nunca se envía al navegador.",
   'de_DE':"Wird serverseitig verwendet, um Tokens bei Cloudflare zu überprüfen. Wird nie an den Browser gesendet.",
   'zh_CN':"在服务器端用于通过 Cloudflare 验证令牌。绝不会发送到浏览器。"},
 "Forms": {'fr_FR':"Formulaires", 'es_ES':"Formularios", 'de_DE':"Formulare", 'zh_CN':"表单"},
 "Data Maker Forms — Upload .dmf": {'fr_FR':"Formulaires Data Maker — Téléverser un .dmf", 'es_ES':"Formularios Data Maker — Subir .dmf", 'de_DE':"Data Maker Formulare — .dmf hochladen", 'zh_CN':"Data Maker 表单 — 上传 .dmf"},
 "Shortcode slug": {'fr_FR':"Slug du shortcode", 'es_ES':"Slug del shortcode", 'de_DE':"Shortcode-Slug", 'zh_CN':"短代码 slug"},
 "Used in the shortcode: %s. Re-uploading the same slug overwrites the form in place.": {
   'fr_FR':"Utilisé dans le shortcode : %s. Re-téléverser le même slug remplace le formulaire sur place.",
   'es_ES':"Se usa en el shortcode: %s. Volver a subir el mismo slug sobrescribe el formulario en su lugar.",
   'de_DE':"Wird im Shortcode verwendet: %s. Ein erneutes Hochladen desselben Slugs überschreibt das Formular an Ort und Stelle.",
   'zh_CN':"用于短代码：%s。重新上传相同的 slug 会就地覆盖该表单。"},
 ".dmf bundle": {'fr_FR':"Fichier .dmf", 'es_ES':"Paquete .dmf", 'de_DE':".dmf-Bundle", 'zh_CN':".dmf 包"},
 "Signature verification is currently %s — change under Settings.": {
   'fr_FR':"La vérification de signature est actuellement %s — modifiable dans les Réglages.",
   'es_ES':"La verificación de firma está actualmente %s — cámbiela en Ajustes.",
   'de_DE':"Die Signaturprüfung ist derzeit %s — änderbar unter Einstellungen.",
   'zh_CN':"签名验证当前为 %s — 可在“设置”中更改。"},
 "ON": {'fr_FR':"ACTIVÉE", 'es_ES':"ACTIVADA", 'de_DE':"EIN", 'zh_CN':"开启"},
 "OFF": {'fr_FR':"DÉSACTIVÉE", 'es_ES':"DESACTIVADA", 'de_DE':"AUS", 'zh_CN':"关闭"},
 "On = render the form the way it looks in the desktop designer (palette, fonts, button variants, heading styles, per-element overrides). Off = strip all of that and let the active WordPress theme drive the look. Per-form; you can flip it later under Forms → Settings.": {
   'fr_FR':"Activé = affiche le formulaire tel qu’il apparaît dans le concepteur de bureau (palette, polices, variantes de boutons, styles de titres, surcharges par élément). Désactivé = supprime tout cela et laisse le thème WordPress actif définir l’apparence. Par formulaire ; vous pouvez le changer ensuite dans Formulaires → Réglages.",
   'es_ES':"Activado = muestra el formulario tal como se ve en el diseñador de escritorio (paleta, fuentes, variantes de botones, estilos de encabezado, anulaciones por elemento). Desactivado = elimina todo eso y deja que el tema de WordPress activo defina el aspecto. Por formulario; puede cambiarlo después en Formularios → Ajustes.",
   'de_DE':"Ein = stellt das Formular so dar, wie es im Desktop-Designer aussieht (Palette, Schriften, Button-Varianten, Überschriftenstile, Überschreibungen pro Element). Aus = entfernt all das und überlässt das Aussehen dem aktiven WordPress-Theme. Pro Formular; Sie können es später unter Formulare → Einstellungen umschalten.",
   'zh_CN':"开启 = 按桌面设计器中的样子渲染表单（调色板、字体、按钮变体、标题样式、按元素覆盖）。关闭 = 去除全部这些，由当前 WordPress 主题决定外观。按表单设置；之后可在“表单 → 设置”中切换。"},
 "Upload form": {'fr_FR':"Téléverser le formulaire", 'es_ES':"Subir formulario", 'de_DE':"Formular hochladen", 'zh_CN':"上传表单"},
 "No file uploaded.": {'fr_FR':"Aucun fichier téléversé.", 'es_ES':"No se subió ningún archivo.", 'de_DE':"Keine Datei hochgeladen.", 'zh_CN':"未上传文件。"},
 "Slug is required.": {'fr_FR':"Le slug est obligatoire.", 'es_ES':"El slug es obligatorio.", 'de_DE':"Der Slug ist erforderlich.", 'zh_CN':"slug 为必填项。"},
 "Uploaded .dmf is larger than the %s limit.": {'fr_FR':"Le .dmf téléversé dépasse la limite de %s.", 'es_ES':"El .dmf subido supera el límite de %s.", 'de_DE':"Die hochgeladene .dmf ist größer als das Limit von %s.", 'zh_CN':"上传的 .dmf 超过了 %s 的上限。"},
 "Could not read the uploaded file.": {'fr_FR':"Impossible de lire le fichier téléversé.", 'es_ES':"No se pudo leer el archivo subido.", 'de_DE':"Die hochgeladene Datei konnte nicht gelesen werden.", 'zh_CN':"无法读取上传的文件。"},
 "Could not parse the .dmf bundle (signature, format, or signing-key mismatch).": {
   'fr_FR':"Impossible d’analyser le fichier .dmf (signature, format ou clé de signature incompatible).",
   'es_ES':"No se pudo analizar el paquete .dmf (signatura, formato o clave de firma no coinciden).",
   'de_DE':"Das .dmf-Bundle konnte nicht ausgewertet werden (Signatur, Format oder Signaturschlüssel stimmen nicht überein).",
   'zh_CN':"无法解析 .dmf 包（签名、格式或签名密钥不匹配）。"},
 "This .dmf was exported in share-only mode (no recipient block). Submissions can't route back to a publisher, so the plugin won't accept it. Sign in to FOBO in the Data Maker desktop app, re-export the form, and try again.": {
   'fr_FR':"Ce .dmf a été exporté en mode partage seul (aucun bloc destinataire). Les soumissions ne peuvent pas être renvoyées à un éditeur, le plugin ne l’accepte donc pas. Connectez-vous à FOBO dans l’application de bureau Data Maker, ré-exportez le formulaire, puis réessayez.",
   'es_ES':"Este .dmf se exportó en modo solo compartir (sin bloque de destinatario). Los envíos no pueden enrutarse de vuelta a un editor, por lo que el plugin no lo aceptará. Inicie sesión en FOBO en la aplicación de escritorio de Data Maker, vuelva a exportar el formulario e inténtelo de nuevo.",
   'de_DE':"Diese .dmf wurde im Nur-Teilen-Modus exportiert (kein Empfängerblock). Übermittlungen können nicht an einen Herausgeber zurückgeleitet werden, daher akzeptiert das Plugin sie nicht. Melden Sie sich in der Data-Maker-Desktop-App bei FOBO an, exportieren Sie das Formular erneut und versuchen Sie es noch einmal.",
   'zh_CN':"此 .dmf 是以仅分享模式导出的（没有收件人块）。提交内容无法回送给发布者，因此插件不会接受它。请在 Data Maker 桌面应用中登录 FOBO，重新导出该表单，然后再试。"},
 "Form saved (id #%1$d). Embed it with: %2$s": {
   'fr_FR':"Formulaire enregistré (id n°%1$d). Intégrez-le avec : %2$s",
   'es_ES':"Formulario guardado (id n.º %1$d). Insértelo con: %2$s",
   'de_DE':"Formular gespeichert (ID #%1$d). Betten Sie es ein mit: %2$s",
   'zh_CN':"表单已保存（id #%1$d）。使用以下方式嵌入：%2$s"},
 "Data Maker Form": {'fr_FR':"Formulaire Data Maker", 'es_ES':"Formulario Data Maker", 'de_DE':"Data Maker Formular", 'zh_CN':"Data Maker 表单"},
 "Render a Data Maker form uploaded under Data Maker Forms → Upload .dmf.": {
   'fr_FR':"Affiche un formulaire Data Maker téléversé dans Formulaires Data Maker → Téléverser un .dmf.",
   'es_ES':"Muestra un formulario Data Maker subido en Formularios Data Maker → Subir .dmf.",
   'de_DE':"Stellt ein Data-Maker-Formular dar, das unter Data Maker Formulare → .dmf hochladen hochgeladen wurde.",
   'zh_CN':"渲染在“Data Maker 表单 → 上传 .dmf”中上传的 Data Maker 表单。"},
 "Pick a form in the block sidebar.": {'fr_FR':"Choisissez un formulaire dans la barre latérale du bloc.", 'es_ES':"Elija un formulario en la barra lateral del bloque.", 'de_DE':"Wählen Sie ein Formular in der Block-Seitenleiste aus.", 'zh_CN':"在区块侧边栏中选择一个表单。"},
 "Required": {'fr_FR':"Obligatoire", 'es_ES':"Obligatorio", 'de_DE':"Erforderlich", 'zh_CN':"必填"},
 "Minimum length (%d)": {'fr_FR':"Longueur minimale (%d)", 'es_ES':"Longitud mínima (%d)", 'de_DE':"Mindestlänge (%d)", 'zh_CN':"最小长度（%d）"},
 "Maximum length (%d)": {'fr_FR':"Longueur maximale (%d)", 'es_ES':"Longitud máxima (%d)", 'de_DE':"Maximale Länge (%d)", 'zh_CN':"最大长度（%d）"},
 "Pattern match": {'fr_FR':"Correspondance de motif", 'es_ES':"Coincidencia de patrón", 'de_DE':"Musterabgleich", 'zh_CN':"模式匹配"},
 "Value does not match the required pattern.": {'fr_FR':"La valeur ne correspond pas au motif requis.", 'es_ES':"El valor no coincide con el patrón requerido.", 'de_DE':"Der Wert entspricht nicht dem erforderlichen Muster.", 'zh_CN':"值与所需的模式不匹配。"},
 "Email format": {'fr_FR':"Format d’e-mail", 'es_ES':"Formato de correo", 'de_DE':"E-Mail-Format", 'zh_CN':"电子邮件格式"},
 "Not a valid email address.": {'fr_FR':"Adresse e-mail non valide.", 'es_ES':"No es una dirección de correo válida.", 'de_DE':"Keine gültige E-Mail-Adresse.", 'zh_CN':"不是有效的电子邮件地址。"},
 "URL format": {'fr_FR':"Format d’URL", 'es_ES':"Formato de URL", 'de_DE':"URL-Format", 'zh_CN':"URL 格式"},
 "Not a valid URL.": {'fr_FR':"URL non valide.", 'es_ES':"No es una URL válida.", 'de_DE':"Keine gültige URL.", 'zh_CN':"不是有效的 URL。"},
 "Phone format": {'fr_FR':"Format de téléphone", 'es_ES':"Formato de teléfono", 'de_DE':"Telefonformat", 'zh_CN':"电话格式"},
 "Not a valid phone number.": {'fr_FR':"Numéro de téléphone non valide.", 'es_ES':"No es un número de teléfono válido.", 'de_DE':"Keine gültige Telefonnummer.", 'zh_CN':"不是有效的电话号码。"},
 "Whole number": {'fr_FR':"Nombre entier", 'es_ES':"Número entero", 'de_DE':"Ganze Zahl", 'zh_CN':"整数"},
 "Not a whole number.": {'fr_FR':"Ce n’est pas un nombre entier.", 'es_ES':"No es un número entero.", 'de_DE':"Keine ganze Zahl.", 'zh_CN':"不是整数。"},
 "Decimal number": {'fr_FR':"Nombre décimal", 'es_ES':"Número decimal", 'de_DE':"Dezimalzahl", 'zh_CN':"小数"},
 "Not a valid decimal number.": {'fr_FR':"Nombre décimal non valide.", 'es_ES':"No es un número decimal válido.", 'de_DE':"Keine gültige Dezimalzahl.", 'zh_CN':"不是有效的小数。"},
 "Monetary amount": {'fr_FR':"Montant monétaire", 'es_ES':"Importe monetario", 'de_DE':"Geldbetrag", 'zh_CN':"货币金额"},
 "Not a valid monetary amount.": {'fr_FR':"Montant monétaire non valide.", 'es_ES':"No es un importe monetario válido.", 'de_DE':"Kein gültiger Geldbetrag.", 'zh_CN':"不是有效的货币金额。"},
 "Date": {'fr_FR':"Date", 'es_ES':"Fecha", 'de_DE':"Datum", 'zh_CN':"日期"},
 "Not a valid date.": {'fr_FR':"Date non valide.", 'es_ES':"No es una fecha válida.", 'de_DE':"Kein gültiges Datum.", 'zh_CN':"不是有效的日期。"},
 "Date-time": {'fr_FR':"Date-heure", 'es_ES':"Fecha y hora", 'de_DE':"Datum-Zeit", 'zh_CN':"日期时间"},
 "Not a valid date-time.": {'fr_FR':"Date-heure non valide.", 'es_ES':"No es una fecha y hora válidas.", 'de_DE':"Kein gültiges Datum-Zeit.", 'zh_CN':"不是有效的日期时间。"},
 "Boolean": {'fr_FR':"Booléen", 'es_ES':"Booleano", 'de_DE':"Boolesch", 'zh_CN':"布尔值"},
 "Not a boolean.": {'fr_FR':"Ce n’est pas un booléen.", 'es_ES':"No es un booleano.", 'de_DE':"Kein boolescher Wert.", 'zh_CN':"不是布尔值。"},
 "Allowed choice": {'fr_FR':"Choix autorisé", 'es_ES':"Opción permitida", 'de_DE':"Zulässige Auswahl", 'zh_CN':"允许的选项"},
 "Value is not in the allowed list.": {'fr_FR':"La valeur ne figure pas dans la liste autorisée.", 'es_ES':"El valor no está en la lista permitida.", 'de_DE':"Der Wert ist nicht in der zulässigen Liste enthalten.", 'zh_CN':"值不在允许的列表中。"},
 "Allowed choices": {'fr_FR':"Choix autorisés", 'es_ES':"Opciones permitidas", 'de_DE':"Zulässige Auswahlen", 'zh_CN':"允许的多个选项"},
 "Some items are not in the allowed list.": {'fr_FR':"Certains éléments ne figurent pas dans la liste autorisée.", 'es_ES':"Algunos elementos no están en la lista permitida.", 'de_DE':"Einige Einträge sind nicht in der zulässigen Liste enthalten.", 'zh_CN':"部分项不在允许的列表中。"},
 "Latitude range": {'fr_FR':"Plage de latitude", 'es_ES':"Rango de latitud", 'de_DE':"Breitengradbereich", 'zh_CN':"纬度范围"},
 "Latitude must be between -90 and 90.": {'fr_FR':"La latitude doit être comprise entre -90 et 90.", 'es_ES':"La latitud debe estar entre -90 y 90.", 'de_DE':"Der Breitengrad muss zwischen -90 und 90 liegen.", 'zh_CN':"纬度必须介于 -90 和 90 之间。"},
 "Longitude range": {'fr_FR':"Plage de longitude", 'es_ES':"Rango de longitud", 'de_DE':"Längengradbereich", 'zh_CN':"经度范围"},
 "Longitude must be between -180 and 180.": {'fr_FR':"La longitude doit être comprise entre -180 et 180.", 'es_ES':"La longitud debe estar entre -180 y 180.", 'de_DE':"Der Längengrad muss zwischen -180 und 180 liegen.", 'zh_CN':"经度必须介于 -180 和 180 之间。"},
 "Geo point": {'fr_FR':"Point géographique", 'es_ES':"Punto geográfico", 'de_DE':"Geopunkt", 'zh_CN':"地理坐标点"},
 "Not a valid geo point.": {'fr_FR':"Point géographique non valide.", 'es_ES':"No es un punto geográfico válido.", 'de_DE':"Kein gültiger Geopunkt.", 'zh_CN':"不是有效的地理坐标点。"},
 "File extension": {'fr_FR':"Extension de fichier", 'es_ES':"Extensión de archivo", 'de_DE':"Dateierweiterung", 'zh_CN':"文件扩展名"},
 "File extension not allowed.": {'fr_FR':"Extension de fichier non autorisée.", 'es_ES':"Extensión de archivo no permitida.", 'de_DE':"Dateierweiterung nicht erlaubt.", 'zh_CN':"不允许的文件扩展名。"},
 "Validation banner": {'fr_FR':"Bannière de validation", 'es_ES':"Banner de validación", 'de_DE':"Validierungsbanner", 'zh_CN':"验证提示横幅"},
 "Please fix the highlighted fields before submitting.": {'fr_FR':"Veuillez corriger les champs en surbrillance avant d’envoyer.", 'es_ES':"Corrija los campos resaltados antes de enviar.", 'de_DE':"Bitte korrigieren Sie die hervorgehobenen Felder vor dem Absenden.", 'zh_CN':"请在提交前更正高亮显示的字段。"},
 "Submit": {'fr_FR':"Envoyer", 'es_ES':"Enviar", 'de_DE':"Absenden", 'zh_CN':"提交"},
 "Edit": {'fr_FR':"Modifier", 'es_ES':"Editar", 'de_DE':"Bearbeiten", 'zh_CN':"编辑"},
 "No items": {'fr_FR':"Aucun élément", 'es_ES':"Sin elementos", 'de_DE':"Keine Einträge", 'zh_CN':"无项目"},
 "Add and press Enter": {'fr_FR':"Ajouter et appuyer sur Entrée", 'es_ES':"Añadir y pulsar Intro", 'de_DE':"Hinzufügen und Eingabetaste drücken", 'zh_CN':"添加并按回车"},
 "Click to upload": {'fr_FR':"Cliquer pour téléverser", 'es_ES':"Haga clic para subir", 'de_DE':"Zum Hochladen klicken", 'zh_CN':"点击上传"},
 "No file selected": {'fr_FR':"Aucun fichier sélectionné", 'es_ES':"Ningún archivo seleccionado", 'de_DE':"Keine Datei ausgewählt", 'zh_CN':"未选择文件"},
 "Browse…": {'fr_FR':"Parcourir…", 'es_ES':"Examinar…", 'de_DE':"Durchsuchen…", 'zh_CN':"浏览…"},
 "Clear": {'fr_FR':"Effacer", 'es_ES':"Borrar", 'de_DE':"Löschen", 'zh_CN':"清除"},
 "Please fix the highlighted fields.": {'fr_FR':"Veuillez corriger les champs en surbrillance.", 'es_ES':"Corrija los campos resaltados.", 'de_DE':"Bitte korrigieren Sie die hervorgehobenen Felder.", 'zh_CN':"请更正高亮显示的字段。"},
 "Please tick the consent box to submit.": {'fr_FR':"Veuillez cocher la case de consentement pour envoyer.", 'es_ES':"Marque la casilla de consentimiento para enviar.", 'de_DE':"Bitte kreuzen Sie das Einwilligungskästchen an, um abzusenden.", 'zh_CN':"请勾选同意框后再提交。"},
 "Please complete the challenge to submit.": {'fr_FR':"Veuillez réussir le défi pour envoyer.", 'es_ES':"Complete el desafío para enviar.", 'de_DE':"Bitte schließen Sie die Aufgabe ab, um abzusenden.", 'zh_CN':"请完成验证后再提交。"},
 "Submitted. Redirecting…": {'fr_FR':"Envoyé. Redirection…", 'es_ES':"Enviado. Redirigiendo…", 'de_DE':"Gesendet. Weiterleitung…", 'zh_CN':"已提交。正在跳转…"},
 "Continue editing": {'fr_FR':"Continuer la modification", 'es_ES':"Continuar editando", 'de_DE':"Bearbeitung fortsetzen", 'zh_CN':"继续编辑"},
 "Start over": {'fr_FR':"Recommencer", 'es_ES':"Empezar de nuevo", 'de_DE':"Neu beginnen", 'zh_CN':"重新开始"},
 "You started this form earlier on this browser. Continue editing your previous submission?": {
   'fr_FR':"Vous avez commencé ce formulaire plus tôt sur ce navigateur. Continuer la modification de votre soumission précédente ?",
   'es_ES':"Empezó este formulario antes en este navegador. ¿Continuar editando su envío anterior?",
   'de_DE':"Sie haben dieses Formular zuvor in diesem Browser begonnen. Möchten Sie Ihre vorherige Übermittlung weiter bearbeiten?",
   'zh_CN':"您之前在此浏览器上已开始填写此表单。是否继续编辑您之前的提交？"},
 "This submission is too large to send. Try shrinking large images or removing big attachments, then submit again.": {
   'fr_FR':"Cette soumission est trop volumineuse pour être envoyée. Essayez de réduire les grandes images ou de supprimer les pièces jointes volumineuses, puis renvoyez.",
   'es_ES':"Este envío es demasiado grande para enviarlo. Intente reducir las imágenes grandes o quitar los archivos adjuntos grandes y vuelva a enviar.",
   'de_DE':"Diese Übermittlung ist zu groß zum Senden. Versuchen Sie, große Bilder zu verkleinern oder große Anhänge zu entfernen, und senden Sie dann erneut.",
   'zh_CN':"此提交内容过大，无法发送。请尝试缩小大图片或移除大附件，然后重新提交。"},
 "Network error — please check your connection and try submitting again.": {
   'fr_FR':"Erreur réseau — veuillez vérifier votre connexion et réessayer d’envoyer.",
   'es_ES':"Error de red — compruebe su conexión e intente enviar de nuevo.",
   'de_DE':"Netzwerkfehler — bitte überprüfen Sie Ihre Verbindung und versuchen Sie es erneut.",
   'zh_CN':"网络错误 — 请检查您的连接，然后重试提交。"},
 "This form is no longer available. Please contact the form owner.": {
   'fr_FR':"Ce formulaire n’est plus disponible. Veuillez contacter le propriétaire du formulaire.",
   'es_ES':"Este formulario ya no está disponible. Póngase en contacto con el propietario del formulario.",
   'de_DE':"Dieses Formular ist nicht mehr verfügbar. Bitte wenden Sie sich an den Formularinhaber.",
   'zh_CN':"此表单已不再可用。请联系表单所有者。"},
 "This form is not accepting submissions right now.": {
   'fr_FR':"Ce formulaire n’accepte pas de soumissions pour le moment.",
   'es_ES':"Este formulario no está aceptando envíos en este momento.",
   'de_DE':"Dieses Formular nimmt derzeit keine Übermittlungen an.",
   'zh_CN':"此表单目前不接受提交。"},
 "The form server is unreachable right now. Please try again in a moment.": {
   'fr_FR':"Le serveur du formulaire est injoignable pour le moment. Veuillez réessayer dans un instant.",
   'es_ES':"El servidor del formulario no está accesible en este momento. Inténtelo de nuevo en un momento.",
   'de_DE':"Der Formularserver ist derzeit nicht erreichbar. Bitte versuchen Sie es gleich noch einmal.",
   'zh_CN':"表单服务器当前无法访问。请稍后重试。"},
 "Something went wrong sending your submission. Please try again — if it keeps happening, contact the form owner.": {
   'fr_FR':"Une erreur s’est produite lors de l’envoi de votre soumission. Veuillez réessayer — si le problème persiste, contactez le propriétaire du formulaire.",
   'es_ES':"Algo salió mal al enviar su envío. Inténtelo de nuevo — si sigue ocurriendo, póngase en contacto con el propietario del formulario.",
   'de_DE':"Beim Senden Ihrer Übermittlung ist etwas schiefgelaufen. Bitte versuchen Sie es erneut — wenn es weiterhin auftritt, wenden Sie sich an den Formularinhaber.",
   'zh_CN':"发送您的提交时出错。请重试 — 若持续出现，请联系表单所有者。"},
 "Submission failed": {'fr_FR':"Échec de l’envoi", 'es_ES':"Error en el envío", 'de_DE':"Übermittlung fehlgeschlagen", 'zh_CN':"提交失败"},
 "slug + hash required.": {'fr_FR':"slug + hash requis.", 'es_ES':"se requieren slug + hash.", 'de_DE':"slug + hash erforderlich.", 'zh_CN':"需要 slug + hash。"},
 "hash must be 64-char lowercase hex SHA-256.": {'fr_FR':"le hash doit être un SHA-256 hexadécimal en minuscules de 64 caractères.", 'es_ES':"el hash debe ser un SHA-256 hexadecimal en minúsculas de 64 caracteres.", 'de_DE':"hash muss ein 64-stelliger SHA-256-Wert in Hex-Kleinbuchstaben sein.", 'zh_CN':"hash 必须是 64 个字符的小写十六进制 SHA-256。"},
 "form not found.": {'fr_FR':"formulaire introuvable.", 'es_ES':"formulario no encontrado.", 'de_DE':"Formular nicht gefunden.", 'zh_CN':"未找到表单。"},
 "form has no recipient configured.": {'fr_FR':"aucun destinataire n’est configuré pour le formulaire.", 'es_ES':"el formulario no tiene un destinatario configurado.", 'de_DE':"Für das Formular ist kein Empfänger konfiguriert.", 'zh_CN':"表单未配置收件人。"},
 "Too many uploads. Please wait a moment.": {'fr_FR':"Trop de téléversements. Veuillez patienter un instant.", 'es_ES':"Demasiadas subidas. Espere un momento.", 'de_DE':"Zu viele Uploads. Bitte warten Sie einen Moment.", 'zh_CN':"上传次数过多。请稍候。"},
 "Data Maker API URL not configured.": {'fr_FR':"URL de l’API Data Maker non configurée.", 'es_ES':"URL de la API de Data Maker no configurada.", 'de_DE':"Data-Maker-API-URL nicht konfiguriert.", 'zh_CN':"未配置 Data Maker API URL。"},
 "libsodium PHP extension required.": {'fr_FR':"extension PHP libsodium requise.", 'es_ES':"se requiere la extensión PHP libsodium.", 'de_DE':"PHP-Erweiterung libsodium erforderlich.", 'zh_CN':"需要 libsodium PHP 扩展。"},
 "Submission too large.": {'fr_FR':"Soumission trop volumineuse.", 'es_ES':"Envío demasiado grande.", 'de_DE':"Übermittlung zu groß.", 'zh_CN':"提交内容过大。"},
 "slug + values required.": {'fr_FR':"slug + values requis.", 'es_ES':"se requieren slug + values.", 'de_DE':"slug + values erforderlich.", 'zh_CN':"需要 slug + values。"},
 "form has no recipient configured; submissions are not supported.": {'fr_FR':"aucun destinataire n’est configuré pour le formulaire ; les soumissions ne sont pas prises en charge.", 'es_ES':"el formulario no tiene un destinatario configurado; los envíos no son compatibles.", 'de_DE':"Für das Formular ist kein Empfänger konfiguriert; Übermittlungen werden nicht unterstützt.", 'zh_CN':"表单未配置收件人；不支持提交。"},
 "Submission rejected.": {'fr_FR':"Soumission rejetée.", 'es_ES':"Envío rechazado.", 'de_DE':"Übermittlung abgelehnt.", 'zh_CN':"提交被拒绝。"},
 "Consent is required before submitting.": {'fr_FR':"Le consentement est requis avant l’envoi.", 'es_ES':"Se requiere consentimiento antes de enviar.", 'de_DE':"Vor dem Absenden ist eine Einwilligung erforderlich.", 'zh_CN':"提交前需要同意。"},
 "Challenge verification failed. Please try again.": {'fr_FR':"Échec de la vérification du défi. Veuillez réessayer.", 'es_ES':"Falló la verificación del desafío. Inténtelo de nuevo.", 'de_DE':"Aufgabenprüfung fehlgeschlagen. Bitte versuchen Sie es erneut.", 'zh_CN':"验证失败。请重试。"},
 "Too many submissions. Please wait a moment.": {'fr_FR':"Trop de soumissions. Veuillez patienter un instant.", 'es_ES':"Demasiados envíos. Espere un momento.", 'de_DE':"Zu viele Übermittlungen. Bitte warten Sie einen Moment.", 'zh_CN':"提交次数过多。请稍候。"},
 "Data Maker API URL is not allowed.": {'fr_FR':"L’URL de l’API Data Maker n’est pas autorisée.", 'es_ES':"La URL de la API de Data Maker no está permitida.", 'de_DE':"Die Data-Maker-API-URL ist nicht zulässig.", 'zh_CN':"不允许的 Data Maker API URL。"},
 # ── Block editor (assets/block.js) ──
 "Render a form uploaded under Data Maker Forms → Upload .dmf.": {
   'fr_FR':"Affiche un formulaire téléversé dans Formulaires Data Maker → Téléverser un .dmf.",
   'es_ES':"Muestra un formulario subido en Formularios Data Maker → Subir .dmf.",
   'de_DE':"Stellt ein Formular dar, das unter Data Maker Formulare → .dmf hochladen hochgeladen wurde.",
   'zh_CN':"渲染在“Data Maker 表单 → 上传 .dmf”中上传的表单。"},
 "— Select a form —": {'fr_FR':"— Sélectionner un formulaire —", 'es_ES':"— Seleccionar un formulario —", 'de_DE':"— Formular auswählen —", 'zh_CN':"— 选择表单 —"},
 "Could not load forms.": {'fr_FR':"Impossible de charger les formulaires.", 'es_ES':"No se pudieron cargar los formularios.", 'de_DE':"Formulare konnten nicht geladen werden.", 'zh_CN':"无法加载表单。"},
 "Inherit form setting": {'fr_FR':"Hériter du réglage du formulaire", 'es_ES':"Heredar el ajuste del formulario", 'de_DE':"Formulareinstellung übernehmen", 'zh_CN':"继承表单设置"},
 "Always apply designer styling": {'fr_FR':"Toujours appliquer le style du concepteur", 'es_ES':"Aplicar siempre el estilo del diseñador", 'de_DE':"Designer-Styling immer anwenden", 'zh_CN':"始终应用设计器样式"},
 "Always strip designer styling": {'fr_FR':"Toujours supprimer le style du concepteur", 'es_ES':"Eliminar siempre el estilo del diseñador", 'de_DE':"Designer-Styling immer entfernen", 'zh_CN':"始终去除设计器样式"},
 "Form": {'fr_FR':"Formulaire", 'es_ES':"Formulario", 'de_DE':"Formular", 'zh_CN':"表单"},
 "Uploaded form": {'fr_FR':"Formulaire téléversé", 'es_ES':"Formulario subido", 'de_DE':"Hochgeladenes Formular", 'zh_CN':"已上传的表单"},
 "Upload more forms under Data Maker Forms → Upload .dmf.": {
   'fr_FR':"Téléversez d’autres formulaires dans Formulaires Data Maker → Téléverser un .dmf.",
   'es_ES':"Suba más formularios en Formularios Data Maker → Subir .dmf.",
   'de_DE':"Laden Sie weitere Formulare unter Data Maker Formulare → .dmf hochladen hoch.",
   'zh_CN':"在“Data Maker 表单 → 上传 .dmf”中上传更多表单。"},
 "Designer styling override": {'fr_FR':"Surcharge du style du concepteur", 'es_ES':"Anulación del estilo del diseñador", 'de_DE':"Designer-Styling überschreiben", 'zh_CN':"设计器样式覆盖"},
 "Layout always honors the form. This only flips colors / fonts / button styling from the Data Maker designer.": {
   'fr_FR':"La mise en page respecte toujours le formulaire. Ceci ne change que les couleurs / polices / styles de boutons issus du concepteur Data Maker.",
   'es_ES':"El diseño siempre respeta el formulario. Esto solo cambia los colores / fuentes / estilo de botones del diseñador de Data Maker.",
   'de_DE':"Das Layout richtet sich immer nach dem Formular. Dies ändert nur Farben / Schriften / Button-Styling aus dem Data-Maker-Designer.",
   'zh_CN':"布局始终遵循表单。此项仅切换来自 Data Maker 设计器的颜色／字体／按钮样式。"},
 "Upload a .dmf": {'fr_FR':"Téléverser un .dmf", 'es_ES':"Subir un .dmf", 'de_DE':"Eine .dmf hochladen", 'zh_CN':"上传 .dmf"},
 "Slug: ": {'fr_FR':"Slug : ", 'es_ES':"Slug: ", 'de_DE':"Slug: ", 'zh_CN':"Slug："},
 "Pick a form from the sidebar.": {'fr_FR':"Choisissez un formulaire dans la barre latérale.", 'es_ES':"Elija un formulario en la barra lateral.", 'de_DE':"Wählen Sie ein Formular in der Seitenleiste aus.", 'zh_CN':"从侧边栏选择一个表单。"},
}

# Plural entries: english singular -> {locale: (one, other)}; zh single.
TP = {
 "Must be at least %d character.": {
   'fr_FR': ("Doit comporter au moins %d caractère.", "Doit comporter au moins %d caractères."),
   'es_ES': ("Debe tener al menos %d carácter.", "Debe tener al menos %d caracteres."),
   'de_DE': ("Muss mindestens %d Zeichen lang sein.", "Muss mindestens %d Zeichen lang sein."),
   'zh_CN': ("长度至少为 %d 个字符。",),
 },
 "Must be at most %d character.": {
   'fr_FR': ("Doit comporter au plus %d caractère.", "Doit comporter au plus %d caractères."),
   'es_ES': ("Debe tener como máximo %d carácter.", "Debe tener como máximo %d caracteres."),
   'de_DE': ("Darf höchstens %d Zeichen lang sein.", "Darf höchstens %d Zeichen lang sein."),
   'zh_CN': ("长度最多为 %d 个字符。",),
 },
}

def unescape(s):
    return s.replace('\\"','"').replace('\\n','\n').replace('\\t','\t').replace('\\\\','\\')

def escape(s):
    return s.replace('\\','\\\\').replace('"','\\"').replace('\n','\\n').replace('\t','\\t')

def parse_pot(text):
    """Yield dict blocks preserving comment lines (#. #: #,) and the
    msgctxt/msgid/msgid_plural so we can re-emit them verbatim per locale."""
    blocks = re.split(r'\n\n+', text.strip())
    out = []
    for b in blocks:
        lines = b.split('\n')
        if not any(l.startswith('msgid') for l in lines):
            continue
        comments = [l for l in lines if l.startswith('#')]
        ctx = None; mid = None; mpl = None
        m = re.search(r'(?m)^msgctxt "(.*)"$', b)
        if m: ctx = m.group(1)
        m = re.search(r'(?m)^msgid "(.*)"$', b)
        if m: mid = m.group(1)
        m = re.search(r'(?m)^msgid_plural "(.*)"$', b)
        if m: mpl = m.group(1)
        out.append({'comments':comments,'ctx':ctx,'mid':mid,'mpl':mpl})
    return out

def header(locale, lang, plural):
    return (
        'msgid ""\n'
        'msgstr ""\n'
        '"Project-Id-Version: Data Maker Forms 0.1.0\\n"\n'
        '"Report-Msgid-Bugs-To: https://fobo-tools.com/\\n"\n'
        '"PO-Revision-Date: 2026-05-30 00:00+0000\\n"\n'
        f'"Last-Translator: FOBO <support@fobo-tools.com>\\n"\n'
        f'"Language-Team: {lang}\\n"\n'
        f'"Language: {locale}\\n"\n'
        '"MIME-Version: 1.0\\n"\n'
        '"Content-Type: text/plain; charset=UTF-8\\n"\n'
        '"Content-Transfer-Encoding: 8bit\\n"\n'
        f'"Plural-Forms: {plural}\\n"\n'
        '"X-Domain: datamaker-renderer\\n"\n'
    )

def main():
    pot = open(POT, encoding='utf-8').read()
    blocks = parse_pot(pot)
    for locale,(lang,plural) in LOCALES.items():
        chunks = [header(locale, lang, plural)]
        for blk in blocks:
            if blk['mid'] is None:        # header block
                continue
            if blk['mid'] == '' and blk['ctx'] is None:
                continue
            mid_raw = blk['mid']
            mid = unescape(mid_raw)
            lines = list(blk['comments'])
            if blk['ctx'] is not None:
                lines.append(f'msgctxt "{blk["ctx"]}"')
            lines.append(f'msgid "{mid_raw}"')
            if blk['mpl'] is not None:
                lines.append(f'msgid_plural "{blk["mpl"]}"')
                tp = TP.get(mid, {}).get(locale)
                if tp:
                    if len(tp) == 1:
                        lines.append(f'msgstr[0] "{escape(tp[0])}"')
                    else:
                        lines.append(f'msgstr[0] "{escape(tp[0])}"')
                        lines.append(f'msgstr[1] "{escape(tp[1])}"')
                else:
                    n = 1 if plural.startswith('nplurals=1') else 2
                    for i in range(n):
                        lines.append(f'msgstr[{i}] ""')
            else:
                tr = T.get(mid, {}).get(locale, '')
                lines.append(f'msgstr "{escape(tr)}"')
            chunks.append('\n'.join(lines))
        po = '\n\n'.join(chunks) + '\n'
        path = os.path.join(HERE, '..', 'languages', f'datamaker-renderer-{locale}.po')
        open(path, 'w', encoding='utf-8').write(po)
        # quick coverage stat
        total = sum(1 for b in blocks if b['mid'] not in (None,''))
        translated = sum(1 for b in blocks if b['mid'] not in (None,'') and (
            (b['mpl'] is None and T.get(unescape(b['mid']),{}).get(locale)) or
            (b['mpl'] is not None and TP.get(unescape(b['mid']),{}).get(locale))))
        print(f'{locale}: {translated}/{total} translated -> {os.path.basename(path)}')

if __name__ == '__main__':
    main()
