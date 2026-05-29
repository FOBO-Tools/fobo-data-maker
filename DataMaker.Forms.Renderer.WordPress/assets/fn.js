/**
 * Dm — browser-side runtime for the DataMaker `Fn` expression library.
 * JsCompiler emits `Dm.<Name>(args)` calls; this file provides the bodies.
 * Function names and arities mirror DataMaker.Expressions.Functions.Fn so
 * source maps 1-to-1.
 *
 * Semantics notes:
 *  - String comparisons that are OrdinalIgnoreCase in C# use `.toLowerCase()`
 *    equality here. Adequate for ASCII form values; pathological unicode
 *    cases will diverge from the server.
 *  - Regex Match/Capture/Replace use the `i` flag to match C#'s IgnoreCase.
 *  - Date inputs accept anything `Date(...)` parses; outputs are YYYY-MM-DD
 *    so they round-trip through DataMaker date fields.
 */
(function (global) {
  'use strict';

  var Dm = {};

  function isEq(a, b) {
    return String(a).toLowerCase() === String(b).toLowerCase();
  }

  function toStr(v) { return v == null ? '' : String(v); }
  function toArr(v) { return Array.isArray(v) ? v : []; }

  // ── Decompose / Compose ──────────────────────────────────────────
  Dm.Split = function (input, separator) {
    if (input == null || separator == null) return [];
    var parts = String(input).split(String(separator));
    return parts.filter(function (s) { return s.length > 0; });
  };
  Dm.Join     = function (arr, sep)    { return toArr(arr).join(toStr(sep)); };
  Dm.Concat   = function (a, b)        { return toStr(a) + toStr(b); };
  Dm.Concat3  = function (a, b, c)     { return toStr(a) + toStr(b) + toStr(c); };
  Dm.Concat4  = function (a, b, c, d)  { return toStr(a) + toStr(b) + toStr(c) + toStr(d); };

  // ── Select ───────────────────────────────────────────────────────
  Dm.At = function (arr, index) {
    var a = toArr(arr);
    var i = index | 0;
    if (i < 0) i = a.length + i;
    return (i >= 0 && i < a.length) ? a[i] : '';
  };
  Dm.First = function (arr) { var a = toArr(arr); return a.length ? a[0] : ''; };
  Dm.Last  = function (arr) { var a = toArr(arr); return a.length ? a[a.length - 1] : ''; };

  Dm.Take = function (arr, n) {
    var a = toArr(arr); return a.slice(0, Math.min(n | 0, a.length));
  };
  Dm.Skip = function (arr, n) {
    var a = toArr(arr); var k = n | 0;
    return k >= a.length ? [] : a.slice(k);
  };
  Dm.Slice = function (arr, start, count) {
    var a = toArr(arr); var s = start | 0; var c = count | 0;
    if (s < 0) s = Math.max(0, a.length + s);
    if (s >= a.length) return [];
    return a.slice(s, Math.min(s + c, a.length));
  };

  Dm.IndexOf = function (arr, value) {
    var a = toArr(arr); var v = toStr(value);
    for (var i = 0; i < a.length; i++) if (isEq(a[i], v)) return i;
    return -1;
  };
  Dm.Where  = function (arr, value) {
    var v = toStr(value);
    return toArr(arr).filter(function (s) { return isEq(s, v); });
  };
  Dm.Except = function (arr, value) {
    var v = toStr(value);
    return toArr(arr).filter(function (s) { return !isEq(s, v); });
  };

  // ── Branch ───────────────────────────────────────────────────────
  Dm.If        = function (cond, then, otherwise) { return cond ? then : otherwise; };
  Dm.Coalesce  = function (a, b)                  { return toStr(a).length > 0 ? a : b; };
  Dm.Coalesce3 = function (a, b, c) {
    if (toStr(a).length > 0) return a;
    if (toStr(b).length > 0) return b;
    return c;
  };
  Dm.Switch = function (value, case1, result1, def) {
    return isEq(value, case1) ? result1 : def;
  };
  Dm.Switch2 = function (value, c1, r1, c2, r2, def) {
    if (isEq(value, c1)) return r1;
    if (isEq(value, c2)) return r2;
    return def;
  };
  Dm.Switch3 = function (value, c1, r1, c2, r2, c3, r3, def) {
    if (isEq(value, c1)) return r1;
    if (isEq(value, c2)) return r2;
    if (isEq(value, c3)) return r3;
    return def;
  };

  // ── Convert ──────────────────────────────────────────────────────
  Dm.ToInt   = function (s) { var n = parseInt(toStr(s), 10); return Number.isFinite(n) ? n : 0; };
  Dm.ToLong  = function (s) { var n = parseInt(toStr(s), 10); return Number.isFinite(n) ? n : 0; };
  Dm.IntToStr0 = function (n) { return String(n | 0); };
  Dm.IntToStr = function (n, format) {
    var v = n | 0;
    var fmt = toStr(format).trim();
    if (!fmt) return String(v);
    var head = fmt.charAt(0).toUpperCase();
    var width = parseInt(fmt.slice(1), 10);
    if (head === 'D') {
      // D<n>: zero-pad to n digits, preserve sign.
      var sign = v < 0 ? '-' : '';
      var body = Math.abs(v).toString();
      if (Number.isFinite(width) && width > body.length) body = '0'.repeat(width - body.length) + body;
      return sign + body;
    }
    if (head === 'N') {
      // N0..N<n>: thousands separator, n decimal places.
      var digits = Number.isFinite(width) ? Math.max(0, width) : 0;
      return v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });
    }
    if (head === 'F') {
      var d = Number.isFinite(width) ? Math.max(0, width) : 2;
      return v.toFixed(d);
    }
    return String(v);
  };

  // ── Array transforms ─────────────────────────────────────────────
  Dm.Insert = function (arr, index, value) {
    var a = toArr(arr).slice();
    var i = Math.max(0, Math.min(index | 0, a.length));
    a.splice(i, 0, value);
    return a;
  };
  Dm.Remove = function (arr, index) {
    var a = toArr(arr).slice();
    var i = index | 0;
    if (i < 0) i = a.length + i;
    if (i < 0 || i >= a.length) return a;
    a.splice(i, 1);
    return a;
  };
  Dm.Filter = function (arr, value) {
    var v = toStr(value);
    return toArr(arr).filter(function (s) { return !isEq(s, v); });
  };
  Dm.Reverse = function (arr) { return toArr(arr).slice().reverse(); };
  Dm.Sort = function (arr) {
    return toArr(arr).slice().sort(function (a, b) {
      var la = String(a).toLowerCase(), lb = String(b).toLowerCase();
      return la < lb ? -1 : la > lb ? 1 : 0;
    });
  };
  Dm.Distinct = function (arr) {
    var a = toArr(arr); var seen = {}; var out = [];
    for (var i = 0; i < a.length; i++) {
      var k = String(a[i]).toLowerCase();
      if (!seen.hasOwnProperty(k)) { seen[k] = true; out.push(a[i]); }
    }
    return out;
  };
  Dm.Map = function (arr, prefix, suffix) {
    var p = toStr(prefix); var s = toStr(suffix);
    return toArr(arr).map(function (x) { return p + toStr(x) + s; });
  };
  Dm.Union = function (a, b) {
    return Dm.Distinct(toArr(a).concat(toArr(b)));
  };
  Dm.Intersect = function (a, b) {
    var setB = {}; toArr(b).forEach(function (x) { setB[String(x).toLowerCase()] = true; });
    return Dm.Distinct(toArr(a).filter(function (x) { return setB[String(x).toLowerCase()]; }));
  };
  Dm.SetExcept = function (a, b) {
    var setB = {}; toArr(b).forEach(function (x) { setB[String(x).toLowerCase()] = true; });
    return Dm.Distinct(toArr(a).filter(function (x) { return !setB[String(x).toLowerCase()]; }));
  };

  // ── String transforms ────────────────────────────────────────────
  Dm.Lower = function (s) { return toStr(s).toLowerCase(); };
  Dm.Upper = function (s) { return toStr(s).toUpperCase(); };
  Dm.Trim  = function (s) { return toStr(s).trim(); };
  Dm.TitleCase = function (s) {
    return toStr(s).toLowerCase().replace(/(^|\s|[\-_\/])([a-z])/g, function (_, sep, ch) {
      return sep + ch.toUpperCase();
    });
  };
  Dm.Pad = function (s, width, padChar) {
    var v = toStr(s); var w = width | 0;
    if (v.length >= w) return v;
    var c = toStr(padChar).charAt(0) || ' ';
    return c.repeat(w - v.length) + v;
  };
  Dm.PadRight = function (s, width, padChar) {
    var v = toStr(s); var w = width | 0;
    if (v.length >= w) return v;
    var c = toStr(padChar).charAt(0) || ' ';
    return v + c.repeat(w - v.length);
  };
  Dm.Left  = function (s, n) { var v = toStr(s); return v.slice(0, Math.min(n | 0, v.length)); };
  Dm.Right = function (s, n) {
    var v = toStr(s); var k = n | 0;
    return k >= v.length ? v : v.slice(v.length - k);
  };

  // ── Regex ────────────────────────────────────────────────────────
  function compileRegex(pattern) {
    try { return new RegExp(toStr(pattern), 'i'); } catch (_) { return null; }
  }
  Dm.Match = function (input, pattern) {
    var re = compileRegex(pattern); if (!re) return false;
    return re.test(toStr(input));
  };
  Dm.Capture = function (input, pattern, group) {
    var re = compileRegex(pattern); if (!re) return '';
    var m = toStr(input).match(re);
    var g = group | 0;
    return (m && g < m.length) ? (m[g] || '') : '';
  };
  Dm.Replace = function (input, pattern, replacement) {
    try {
      var re = new RegExp(toStr(pattern), 'gi');
      return toStr(input).replace(re, toStr(replacement));
    } catch (_) { return toStr(input); }
  };

  // ── Aggregates ───────────────────────────────────────────────────
  Dm.Len    = function (arr) { return toArr(arr).length; };
  Dm.LenStr = function (s)   { return toStr(s).length; };
  Dm.Any = function (arr, value) {
    var v = toStr(value); return toArr(arr).some(function (x) { return isEq(x, v); });
  };
  Dm.All = function (arr, value) {
    var v = toStr(value); var a = toArr(arr);
    return a.length > 0 && a.every(function (x) { return isEq(x, v); });
  };
  Dm.CountOf = function (arr, value) {
    var v = toStr(value);
    return toArr(arr).reduce(function (n, x) { return n + (isEq(x, v) ? 1 : 0); }, 0);
  };
  Dm.Min  = function (a, b) { return Math.min(a, b); };
  Dm.Max  = function (a, b) { return Math.max(a, b); };
  Dm.MinD = function (a, b) { return Math.min(+a, +b); };
  Dm.MaxD = function (a, b) { return Math.max(+a, +b); };

  // ── Date operations ──────────────────────────────────────────────
  function parseDate(s) {
    if (s == null) return null;
    var d = new Date(String(s));
    return isNaN(d.getTime()) ? null : d;
  }
  function pad2(n) { n = n | 0; return (n < 10 ? '0' : '') + n; }
  function fmtYmd(d) {
    return d.getUTCFullYear() + '-' + pad2(d.getUTCMonth() + 1) + '-' + pad2(d.getUTCDate());
  }
  Dm.Year  = function (s) { var d = parseDate(s); return d ? d.getUTCFullYear() : 0; };
  Dm.Month = function (s) { var d = parseDate(s); return d ? d.getUTCMonth() + 1 : 0; };
  Dm.Day   = function (s) { var d = parseDate(s); return d ? d.getUTCDate() : 0; };
  Dm.AddDays = function (s, days) {
    var d = parseDate(s); if (!d) return toStr(s);
    d.setUTCDate(d.getUTCDate() + (days | 0));
    return fmtYmd(d);
  };
  Dm.DiffDays = function (a, b) {
    var d1 = parseDate(a), d2 = parseDate(b);
    if (!d1 || !d2) return 0;
    return Math.trunc((d1 - d2) / 86400000);
  };
  Dm.Today = function () { return fmtYmd(new Date()); };
  Dm.Now   = function () { return new Date().toISOString(); };

  global.Dm = Dm;
})(typeof window !== 'undefined' ? window : globalThis);
