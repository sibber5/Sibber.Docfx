/* SPDX-License-Identifier: MIT */
/* Copyright (c) 2025-2026 sibber (GitHub: sibber5) */

exports.preTransform = function (model) {
  var alertMap = {
    'NOTE': { css: 'alert-info', title: 'Note' },
    'TIP': { css: 'alert-tip', title: 'Tip' },
    'WARNING': { css: 'alert-warning', title: 'Warning' },
    'IMPORTANT': { css: 'alert-important', title: 'Important' },
    'CAUTION': { css: 'alert-danger', title: 'Caution' },
    'THREADSAFE': { css: 'alert-info', title: 'Thread-Safety: Safe' },
    'THREADUNSAFE': { css: 'alert-danger', title: 'Thread-Safety: Unsafe' }
  };

  function transformString(str) {
    if (typeof str !== "string") return str;
    
    // Only matches <p> wrappers (from <para> tags) that start immediately with <b>NOTE:</b> etc.
    // Content inside the <p> can include <br/> tags.
    var regex = /<p>\s*<(?:b|strong)>\s*(NOTE|TIP|WARNING|IMPORTANT|CAUTION|THREAD-?SAFE|THREAD-?UNSAFE):\s*<\/(?:b|strong)>\s*([\s\S]*?)<\/p>/gi;
    
    return str.replace(regex, function (match, type, content) {
      var upperType = type.toUpperCase().replace(/-/g, '');
      var alert = alertMap[upperType] || alertMap['NOTE'];
      return '<div class="alert ' + alert.css + '"><h5>' + alert.title + '</h5><p>' + content + '</p></div>';
    });
  }

  function traverse(obj) {
    if (!obj || typeof obj !== "object") return;
    
    for (var key in obj) {
      if (typeof obj[key] === "string") {
        obj[key] = transformString(obj[key]);
      } else if (typeof obj[key] === "object") {
        traverse(obj[key]);
      }
    }
  }
  
  traverse(model);
  return model;
};
