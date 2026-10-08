// Trava a rolagem da pagina enquanto houver um modal aberto.
// Sem isto a barra de rolagem continua visivel ao lado do overlay e a tela
// fica com uma faixa que o fundo escurecido nao cobre.
(function () {
    function atualizar() {
        const aberto = !!document.querySelector('.modal-overlay');
        document.body.style.overflow = aberto ? 'hidden' : '';
    }

    function observar() {
        if (!document.body) return;
        new MutationObserver(atualizar).observe(document.body, {
            childList: true,
            subtree: true
        });
        atualizar();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', observar);
    } else {
        observar();
    }
})();

// Tema claro/escuro. O App.razor ja aplicou o tema salvo antes da primeira
// pintura; aqui ficam a leitura e a troca, chamadas pelo NavMenu.
window.tema = {
    ler: function () {
        try {
            var t = localStorage.getItem('tema');
            if (t === 'claro' || t === 'escuro') return t;
        } catch (e) { /* navegacao anonima ou storage bloqueado */ }

        // Sem escolha salva, segue a preferencia do sistema operacional.
        return matchMedia('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro';
    },

    aplicar: function (valor) {
        document.documentElement.setAttribute('data-tema', valor);
        try {
            localStorage.setItem('tema', valor);
        } catch (e) { /* a troca vale para esta sessao mesmo sem persistir */ }
    }
};

// Olhinho de mostrar senha. Ouvinte delegado no documento em vez de um
// onclick no botao: a tela de login e renderizacao estatica e a navegacao
// aprimorada do Blazor troca o DOM, entao um ouvinte preso ao elemento
// se perderia na primeira navegacao.
document.addEventListener('click', function (evento) {
    const botao = evento.target.closest('[data-ver-senha]');
    if (!botao) return;

    const campo = document.getElementById(botao.dataset.verSenha);
    if (!campo) return;

    const mostrando = campo.type === 'text';
    campo.type = mostrando ? 'password' : 'text';

    botao.setAttribute('aria-pressed', String(!mostrando));
    botao.setAttribute('aria-label', mostrando ? 'Mostrar senha' : 'Ocultar senha');
    botao.classList.toggle('revelado', !mostrando);

    // O clique tira o foco do campo; devolver evita que a pessoa precise
    // clicar de novo para continuar digitando.
    campo.focus();
});
