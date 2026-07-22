using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DataMaker.Sdk;
using NUnit.Framework;
using Sodium;

namespace DataMaker.Sdk.Tests;

[TestFixture]
public class DataMakerSdkTests
{
    // Build a real, signed .dmf in memory so tests exercise the exact verify +
    // seal path a production bundle hits — no fixtures, no network.
    private static (byte[] Bytes, KeyPair Recipient) MakeDmf(bool tamperForm = false, bool withRecipient = true)
    {
        var signer = PublicKeyAuth.GenerateKeyPair();   // Ed25519 — signs the manifest
        var recipient = PublicKeyBox.GenerateKeyPair(); // X25519 — receives submissions

        const string formJson = """
        {
          "id": "contact_form",
          "name": "Contact form",
          "description": "Say hello",
          "schemaVersion": 4,
          "submitPolicy": "Anonymous",
          "fields": [
            { "id": "f1", "name": "email", "label": "Email", "kind": "email", "required": true },
            { "id": "f2", "name": "full_name", "label": "Full name", "kind": "text", "required": false },
            { "id": "f3", "name": "age", "label": "Age", "kind": "number", "required": false },
            { "id": "f4", "name": "subscribe", "label": "Subscribe", "kind": "boolean", "required": false },
            { "id": "f5", "name": "topic", "label": "Topic", "kind": "choice", "required": false,
              "choice": { "allowCustom": false, "choices": [ { "value": "sales", "label": "Sales" }, { "value": "support" } ] } },
            { "id": "f6", "name": "tags", "label": "Tags", "kind": "multi-choice", "required": false,
              "choice": { "allowCustom": false, "choices": [ { "value": "a" }, { "value": "b" }, { "value": "c" } ] } },
            { "id": "f7", "name": "total", "label": "Total", "kind": "calc", "required": false }
          ]
        }
        """;
        var formBytes = Encoding.UTF8.GetBytes(formJson);
        var formHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(formBytes)).ToLowerInvariant();

        var recipientBlock = withRecipient
            ? $$"""{ "publicKey": "{{Convert.ToBase64String(recipient.PublicKey)}}", "userId": "user-123" }"""
            : "null";
        var manifestJson = $$"""
        {
          "envelopeVersion": 3,
          "signedAt": "2026-05-29T00:00:00Z",
          "signer": { "publicKey": "{{Convert.ToBase64String(signer.PublicKey)}}", "identity": { "name": "Tester" } },
          "recipient": {{recipientBlock}},
          "files": [ { "path": "form.json", "sha256": "{{formHash}}", "size": {{formBytes.Length}} } ]
        }
        """;
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var signature = PublicKeyAuth.SignDetached(manifestBytes, signer.PrivateKey);

        var storedForm = tamperForm ? Encoding.UTF8.GetBytes(formJson.Replace("Contact form", "TAMPERED")) : formBytes;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", manifestBytes);
            Write(zip, "signature.bin", signature);
            Write(zip, "form.json", storedForm);
        }
        return (ms.ToArray(), recipient);

        static void Write(ZipArchive zip, string name, byte[] data)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(data, 0, data.Length);
        }
    }

    private static JsonDocument OpenSealed(string ciphertextB64, KeyPair recipient)
    {
        var plain = SealedPublicKeyBox.Open(Convert.FromBase64String(ciphertextB64), recipient.PrivateKey, recipient.PublicKey);
        return JsonDocument.Parse(plain);
    }

    [Test]
    public void ReadForm_verifies_and_extracts()
    {
        var (bytes, _) = MakeDmf();
        var form = DataMakerClient.ReadForm(bytes);

        Assert.That(form.FormId, Is.EqualTo("contact_form"));
        Assert.That(form.SchemaVersion, Is.EqualTo(4));
        Assert.That(form.RecipientUserId, Is.EqualTo("user-123"));
        Assert.That(form.RecipientPublicKey, Is.Not.Null.And.Not.Empty);
        Assert.That(form.Verified, Is.True);

        var topic = form.Fields.Single(f => f.Key == "topic");
        Assert.That(topic.Choices!.Select(c => c.Value), Is.EqualTo(new[] { "sales", "support" }));
    }

    [Test]
    public void BuildSubmission_seals_payload_the_recipient_can_open()
    {
        var (bytes, recipient) = MakeDmf();
        var form = DataMakerClient.ReadForm(bytes);

        var built = DataMakerClient.BuildSubmission(form, new Dictionary<string, object?>
        {
            ["email"] = "ada@example.com",
            ["full_name"] = "Ada",
            ["age"] = "37",       // string in → number out
            ["subscribe"] = "yes", // truthy string → bool true
            ["tags"] = new[] { "a", "c" },
        });

        Assert.That(built.Envelope.FormId, Is.EqualTo("contact_form"));
        Assert.That(built.Envelope.RecipientPubkey, Is.EqualTo(form.RecipientPublicKey));
        Assert.That(built.Envelope.SubmitterId, Is.Null);
        Assert.That(built.Envelope.SubmissionId.Length, Is.EqualTo(32)); // GUID "N"

        using var opened = OpenSealed(built.Envelope.Ciphertext, recipient);
        var root = opened.RootElement;
        Assert.That(root.GetProperty("formVersion").GetInt32(), Is.EqualTo(4));
        Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo("Create"));
        Assert.That(root.GetProperty("formSchema").GetString(), Is.EqualTo(""));
        var values = root.GetProperty("values");
        Assert.That(values.GetProperty("email").GetString(), Is.EqualTo("ada@example.com"));
        Assert.That(values.GetProperty("age").GetInt64(), Is.EqualTo(37));
        Assert.That(values.GetProperty("subscribe").GetBoolean(), Is.True);
        Assert.That(values.GetProperty("tags").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "a", "c" }));
    }

    // The terminal renderer submits with Validate = false (it validates itself),
    // so array values reach serialization as the raw string[] the form stored —
    // not the List<string> the coercion path produces. Guards that the array
    // still survives the sealed-payload round-trip on that path.
    [Test]
    public void BuildSubmission_without_validation_preserves_array_values()
    {
        var (bytes, recipient) = MakeDmf();
        var form = DataMakerClient.ReadForm(bytes);

        var built = DataMakerClient.BuildSubmission(form, new Dictionary<string, object?>
        {
            ["email"] = "a@b.com",
            ["tags"]  = new[] { "a", "c" },   // string[], exactly what the terminal stores
        }, new SubmitOptions { Validate = false });

        using var opened = OpenSealed(built.Envelope.Ciphertext, recipient);
        var values = opened.RootElement.GetProperty("values");
        Assert.That(values.GetProperty("tags").EnumerateArray().Select(e => e.GetString()),
            Is.EqualTo(new[] { "a", "c" }));
    }

    // Callers (e.g. the terminal) pre-serialise complex field types the SDK
    // doesn't know — signature / image refs — into a JsonElement via their own
    // converter, then hand that through. Guards that a JsonElement value seals
    // and re-opens verbatim (regressed once: "no metadata for JsonElement").
    [Test]
    public void BuildSubmission_passes_jsonelement_values_through_verbatim()
    {
        var (bytes, recipient) = MakeDmf();
        var form = DataMakerClient.ReadForm(bytes);

        using var doc = System.Text.Json.JsonDocument.Parse("{\"typedName\":\"Ada\",\"owned\":true}");
        var built = DataMakerClient.BuildSubmission(form, new Dictionary<string, object?>
        {
            ["email"]     = "a@b.com",
            ["signature"] = doc.RootElement.Clone(),
        }, new SubmitOptions { Validate = false });

        using var opened = OpenSealed(built.Envelope.Ciphertext, recipient);
        var sig = opened.RootElement.GetProperty("values").GetProperty("signature");
        Assert.That(sig.GetProperty("typedName").GetString(), Is.EqualTo("Ada"));
        Assert.That(sig.GetProperty("owned").GetBoolean(), Is.True);
    }

    [Test]
    public void Validation_rejects_missing_required_unknown_readonly_and_bad_choice()
    {
        var (bytes, _) = MakeDmf();
        var form = DataMakerClient.ReadForm(bytes);

        var e1 = Assert.Throws<ValidationException>(() => DataMakerClient.BuildSubmission(form, new Dictionary<string, object?> { ["full_name"] = "NoEmail" }));
        Assert.That(e1!.Issues.Any(i => i.Field == "email"), Is.True);

        var e2 = Assert.Throws<ValidationException>(() => DataMakerClient.BuildSubmission(form, new Dictionary<string, object?> { ["email"] = "a@b.com", ["nope"] = "x" }));
        Assert.That(e2!.Issues.Any(i => i.Field == "nope"), Is.True);

        var e3 = Assert.Throws<ValidationException>(() => DataMakerClient.BuildSubmission(form, new Dictionary<string, object?> { ["email"] = "a@b.com", ["total"] = 5 }));
        Assert.That(e3!.Issues.Any(i => i.Field == "total" && i.Message.Contains("read-only")), Is.True);

        var e4 = Assert.Throws<ValidationException>(() => DataMakerClient.BuildSubmission(form, new Dictionary<string, object?> { ["email"] = "a@b.com", ["topic"] = "marketing" }));
        Assert.That(e4!.Issues.Any(i => i.Field == "topic"), Is.True);
    }

    [Test]
    public void Tampered_form_fails_hash_check()
    {
        var (bytes, _) = MakeDmf(tamperForm: true);
        var e = Assert.Throws<DmfException>(() => DataMakerClient.ReadForm(bytes));
        Assert.That(e!.Code, Is.EqualTo("DMF_INVALID"));
    }

    [Test]
    public void Share_only_bundle_cannot_submit()
    {
        var (bytes, _) = MakeDmf(withRecipient: false);
        var form = DataMakerClient.ReadForm(bytes);
        Assert.That(form.RecipientPublicKey, Is.Null);
        var e = Assert.Throws<DataMakerException>(() => DataMakerClient.BuildSubmission(form, new Dictionary<string, object?> { ["email"] = "a@b.com" }));
        Assert.That(e!.Code, Is.EqualTo("NO_RECIPIENT"));
    }

    [Test]
    public async Task SubmitAsync_posts_and_returns_result()
    {
        var (bytes, recipient) = MakeDmf();
        var handler = new StubHandler(HttpStatusCode.OK, """{ "submissionId": "srv-id", "editToken": "tok-abc" }""");
        var client = new DataMakerClient(new HttpClient(handler), "https://example.test/");

        var result = await client.SubmitAsync(bytes, new Dictionary<string, object?> { ["email"] = "ada@example.com" });

        Assert.That(handler.RequestUri, Is.EqualTo("https://example.test/submissions"));
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.That(sent.RootElement.GetProperty("formId").GetString(), Is.EqualTo("contact_form"));
        var ct = sent.RootElement.GetProperty("ciphertext").GetString()!;
        using var opened = OpenSealed(ct, recipient);
        Assert.That(opened.RootElement.GetProperty("values").GetProperty("email").GetString(), Is.EqualTo("ada@example.com"));

        Assert.That(result.EditToken, Is.EqualTo("tok-abc"));
        Assert.That(result.FormId, Is.EqualTo("contact_form"));
    }

    [Test]
    public void SubmitAsync_throws_on_non_2xx()
    {
        var (bytes, _) = MakeDmf();
        var handler = new StubHandler(HttpStatusCode.RequestEntityTooLarge, "too big");
        var client = new DataMakerClient(new HttpClient(handler), "https://example.test/");

        var e = Assert.ThrowsAsync<SubmissionException>(() =>
            client.SubmitAsync(bytes, new Dictionary<string, object?> { ["email"] = "a@b.com" }));
        Assert.That(e!.Status, Is.EqualTo(413));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public string? RequestUri;
        public string? RequestBody;

        public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
