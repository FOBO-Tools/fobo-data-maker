// File-binary upload path for Zapier-side file inputs. Zapier hydrates
// file inputs as URLs; we fetch the bytes, hash them, mint a pre-signed
// PUT slot via the anon /submissions/upload-slot endpoint, PUT the
// bytes, then return an ImageRef/AttachmentRef-shape descriptor for
// the submission payload's values dict.
//
// This is the same flow the web renderer + WP plugin run on submit —
// the desktop receiver sees a URL-only blob and pulls bytes from S3
// on demand. No bytes ever travel through the Lambda envelope.

const { sha256Hex } = require('./encrypt');

const SYNC_BASE = 'https://datamaker-api.fobo-tools.com';

/**
 * Upload one Zapier file input to FOBO submission storage.
 *
 * @param z          — Zapier platform-core z helper (z.request, z.JSON)
 * @param fileUrl    — URL Zapier supplied for the file input
 * @param recipient  — recipientUserId from the published form
 * @returns ImageRef / AttachmentRef shape: { url, hash, fileName, mime, sizeBytes, owned }
 */
async function uploadBlob(z, fileUrl, recipient) {
  // Download Zapier's file URL — z.request handles auth, redirects, +
  // returns raw bytes when `raw: true` is set.
  const fileResp = await z.request({
    url:     fileUrl,
    method:  'GET',
    raw:     true,
  });
  if (fileResp.status < 200 || fileResp.status >= 300)
    throw new z.errors.Error(`Couldn't fetch Zapier file URL: ${fileResp.status}`, 'BlobFetchError', fileResp.status);

  // platform-core's raw mode hands us a stream — buffer it so we can
  // hash + upload in one pass. (chunked uploads aren't worth the
  // complexity for ≤ 50 MB blobs the slot accepts.)
  const buffer = await streamToBuffer(fileResp.body);
  const hash   = await sha256Hex(buffer);

  const mime     = sniffMime(fileResp.headers, fileUrl);
  const fileName = sniffFileName(fileResp.headers, fileUrl);

  // Reserve a presigned-PUT slot. Same shape sync Lambda's
  // SubmissionBlobsController.UploadSlotRequest expects.
  const slotResp = await z.request({
    url:    `${SYNC_BASE}/submissions/upload-slot`,
    method: 'POST',
    body: {
      recipientUserId: recipient,
      hash,
      mime,
      sizeBytes:       buffer.length,
    },
    headers: { 'content-type': 'application/json' },
  });
  if (slotResp.status < 200 || slotResp.status >= 300)
    throw new z.errors.Error(`upload-slot failed: ${slotResp.status} ${slotResp.content}`, 'UploadSlotError', slotResp.status);
  const slot = z.JSON.parse(slotResp.content);

  // PUT bytes directly to S3. The slot includes the Content-Type
  // header binding so PutObject signature stays valid.
  const putResp = await z.request({
    url:     slot.url,
    method:  'PUT',
    body:    buffer,
    headers: { 'content-type': mime || 'application/octet-stream' },
    raw:     true,
    skipThrowForStatus: true,
  });
  if (putResp.status < 200 || putResp.status >= 300)
    throw new z.errors.Error(`S3 PUT failed: ${putResp.status}`, 'S3PutError', putResp.status);

  // ImageRef / AttachmentRef shape the desktop receiver expects.
  // Note: we return the slot.url (the upload presigned URL) only as
  // a transient reference; the receiver re-derives a fresh GET URL
  // on demand via /submissions/blob/{userId}/{hash}. The url field
  // in the ref is bookkeeping only — the (recipientUserId, hash)
  // pair is the real capability.
  return {
    url:       slot.url,
    hash,
    fileName,
    mime,
    sizeBytes: buffer.length,
    owned:     true,
  };
}

async function streamToBuffer(stream) {
  // z.request with raw:true returns a Node Readable stream
  if (stream && typeof stream.getReader === 'function') {
    // Web ReadableStream fallback (some Zapier runtimes)
    const reader = stream.getReader();
    const chunks = [];
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      chunks.push(Buffer.from(value));
    }
    return Buffer.concat(chunks);
  }
  const chunks = [];
  for await (const chunk of stream) chunks.push(Buffer.from(chunk));
  return Buffer.concat(chunks);
}

function sniffMime(headers, url) {
  const ct = (headers && (headers['content-type'] || headers['Content-Type'])) || '';
  if (ct) return String(ct).split(';')[0].trim();
  // Last-ditch from URL extension
  const m = /\.([a-z0-9]+)(?:$|\?)/i.exec(url);
  const ext = m && m[1].toLowerCase();
  return {
    png:  'image/png',
    jpg:  'image/jpeg',
    jpeg: 'image/jpeg',
    gif:  'image/gif',
    pdf:  'application/pdf',
    csv:  'text/csv',
    json: 'application/json',
    txt:  'text/plain',
  }[ext] || 'application/octet-stream';
}

function sniffFileName(headers, url) {
  const cd = (headers && (headers['content-disposition'] || headers['Content-Disposition'])) || '';
  const m  = /filename="?([^";]+)"?/i.exec(cd);
  if (m) return m[1];
  try {
    const u = new URL(url);
    const last = u.pathname.split('/').filter(Boolean).pop();
    return last || 'file';
  } catch {
    return 'file';
  }
}

module.exports = { uploadBlob };
