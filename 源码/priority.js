(function (root) {
 'use strict';
 const P = typeof module === 'object' ? require('./production.js') : root.P;
 const LIMIT_MS = 48 * 60 * 60 * 1000;

 function pending(orders) {
  return P.groups(orders.filter(order => !order.archived)).flatMap(group => {
   if (!group.some(order => !String(order.machineCode || '').trim())) return [];
   const joinedTimes = group.map(order => {
    const created = Date.parse(order.createdAt);
    return Number.isFinite(created) ? created : Date.parse(order.productionSource?.importedAt);
   }).filter(Number.isFinite);
   if (!joinedTimes.length) return [];
   const joinedAt = Math.min(...joinedTimes);
   return [{order: group[0], joinedAt, deadline: joinedAt + LIMIT_MS}];
  });
 }

 function overdue(orders, now = Date.now()) {
  return pending(orders).filter(item => now >= item.deadline)
   .sort((a, b) => a.deadline - b.deadline);
 }

 const api = {LIMIT_MS, pending, overdue};
 if (typeof module === 'object' && module.exports) module.exports = api;
 else root.Priority = api;
})(typeof globalThis === 'undefined' ? this : globalThis);
