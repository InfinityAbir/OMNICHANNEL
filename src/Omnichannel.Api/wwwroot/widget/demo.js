/* Demo "customer site" page — loads the product chat widget from the API origin (?api=...&slug=...). */
(function () {
  'use strict';
  var params = new URLSearchParams(window.location.search);
  var slug = params.get('slug') || '';
  // The product API origin. On a real customer site this is https://your-api; here the E2E passes
  // it explicitly so the embed is genuinely cross-origin and the browser sends an Origin header.
  var api = params.get('api') || window.location.origin;
  if (!slug) return;
  var s = document.createElement('script');
  s.src = api.replace(/\/$/, '') + '/widget/embed.js';
  s.setAttribute('data-slug', slug);
  s.setAttribute('data-acme-demo', '1');
  document.head.appendChild(s);
})();
