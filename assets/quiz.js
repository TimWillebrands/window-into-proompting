// Shared quiz widget. Markup contract:
// <div class="quiz"><div class="q"><p class="prompt">…</p>
//   <button class="opt" data-correct>…</button><button class="opt">…</button>
//   <div class="expl">…</div></div></div>
// Click → immediate feedback, options lock, explanation reveals.
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.quiz .q').forEach((q) => {
        const opts = q.querySelectorAll('button.opt');
        opts.forEach((btn) => {
            btn.addEventListener('click', () => {
                if (q.classList.contains('answered')) return;
                q.classList.add('answered');
                opts.forEach((b) => {
                    b.disabled = true;
                    if (b.hasAttribute('data-correct')) b.classList.add('correct');
                });
                if (!btn.hasAttribute('data-correct')) btn.classList.add('wrong');
            });
        });
    });
});
