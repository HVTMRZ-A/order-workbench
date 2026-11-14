'use strict';
let priorityTimer = null;

function refreshPriority() {
 const now = Date.now();
 const pending = Priority.pending(activeOrders());
 const overdue = Priority.overdue(activeOrders(), now);
 $('#nav-priority').textContent = overdue.length;
 $('#priority-panel').hidden = view !== 'priority';
 if (view === 'priority') {
  $('.orders-panel').hidden = true;
  $('.heading-import').hidden = true;
  $('#priority-count').textContent = overdue.length + ' 单';
  $('#priority-list').innerHTML = overdue.length ? `<table class="order-table priority-table">
   <thead><tr><th>订单 / 客户</th><th>加入工作台</th><th>料号确认截止</th><th>待办</th><th></th></tr></thead>
   <tbody>${overdue.map(({order, joinedAt, deadline}) => `<tr>
    <td><b class="order-no">${esc(P.alias(order))}</b><div class="order-customer">${esc(P.contract(order))}</div><div class="order-customer">${esc(order.customer)}</div></td>
    <td>${esc(SaveState.timestamp(new Date(joinedAt).toISOString()))}</td>
    <td class="amber-text">${esc(SaveState.timestamp(new Date(deadline).toISOString()))}</td>
    <td><span class="tag amber">机器料号待补齐</span></td>
    <td><button class="secondary small" data-batch-order="${esc(order.id)}">整单确认</button></td>
   </tr>`).join('')}</tbody></table>` : '<div class="empty"><div class="empty-symbol">✓</div><h3>暂无超时未建好机器料号的订单</h3><p>从整单首次加入起满 48 小时，仍缺机器料号的订单会自动列在这里。</p></div>';
 }
 clearTimeout(priorityTimer);
 const deadlines = pending.map(item => item.deadline).filter(deadline => deadline > now);
 if (deadlines.length) {
  const delay = Math.min(Math.min(...deadlines) - now, 2147483647);
  priorityTimer = setTimeout(() => render(), delay);
 }
 return overdue;
}

window.addEventListener('focus', () => { if (dataReady) render(); });
document.addEventListener('visibilitychange', () => {
 if (!document.hidden && dataReady) render();
});
