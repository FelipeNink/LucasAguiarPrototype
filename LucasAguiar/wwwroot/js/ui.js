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

// Apoio do componente Seletor.
//
// Duas coisas que o Blazor sozinho nao resolve: saber que o clique caiu
// fora do componente, e saber se o painel cabe abaixo do gatilho.
window.seletor = {
    // Fecha ao clicar fora. Devolve um id para o componente cancelar o
    // registro quando for descartado, senao o ouvinte vaza a cada
    // dropdown aberto.
    registrarCliqueFora: function (elemento, referenciaDotNet) {
        const aoClicar = function (evento) {
            if (elemento && !elemento.contains(evento.target)) {
                referenciaDotNet.invokeMethodAsync('FecharPorCliqueFora');
            }
        };

        document.addEventListener('mousedown', aoClicar, true);

        const id = 'sel-' + Math.random().toString(36).slice(2);
        this._registros = this._registros || {};
        this._registros[id] = aoClicar;
        return id;
    },

    cancelarCliqueFora: function (id) {
        if (!this._registros || !this._registros[id]) return;
        document.removeEventListener('mousedown', this._registros[id], true);
        delete this._registros[id];
    },

    // true quando falta espaco abaixo e sobra acima: o painel abre para cima.
    abrirParaCima: function (elemento, alturaPainel) {
        if (!elemento) return false;
        const caixa = elemento.getBoundingClientRect();
        const abaixo = window.innerHeight - caixa.bottom;
        return abaixo < alturaPainel && caixa.top > abaixo;
    },

    // Mantem a opcao sob o cursor do teclado visivel na rolagem.
    rolarAteOpcao: function (painel, indice) {
        if (!painel) return;
        const opcao = painel.querySelectorAll('[role="option"]')[indice];
        if (opcao) opcao.scrollIntoView({ block: 'nearest' });
    },

    focar: function (elemento) {
        if (elemento) elemento.focus();
    }
};

// Envio do login: a tela e renderizacao estatica, entao nao ha circuito
// Blazor para desabilitar o botao. Feito aqui, no submit do formulario.
document.addEventListener('submit', function (evento) {
    const botao = evento.target.querySelector('button[type="submit"][data-texto-enviando]');
    if (!botao) return;

    botao.disabled = true;
    botao.textContent = botao.dataset.textoEnviando;
});
