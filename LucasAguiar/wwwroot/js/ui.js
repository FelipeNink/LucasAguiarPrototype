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

// A navegacao aprimorada do Blazor sincroniza os atributos do <html> com o
// HTML vindo do servidor, que nao tem data-tema: o atributo sumia e a tela
// voltava para o claro a cada troca de pagina. O script do <head> nao roda
// de novo nessa navegacao, entao o tema e reaplicado aqui.
(function () {
    const raiz = document.documentElement;

    function reaplicar() {
        const salvo = window.tema.ler();
        if (raiz.getAttribute('data-tema') !== salvo) {
            raiz.setAttribute('data-tema', salvo);
        }
    }

    new MutationObserver(reaplicar).observe(raiz, {
        attributes: true,
        attributeFilter: ['data-tema']
    });

    document.addEventListener('enhancedload', reaplicar);
    reaplicar();
})();

// Atalhos globais: F2 abre a nova venda e F3 o novo agendamento, de qualquer
// tela. Vale tambem digitando num campo: tecla de funcao nao escreve nada.
document.addEventListener('keydown', function (evento) {
    if (evento.ctrlKey || evento.altKey || evento.metaKey || evento.shiftKey) return;

    const destino = { F2: '/venda/nova', F3: '/agendamento/novo' }[evento.key];
    if (!destino) return;

    // Na tela de login nao ha sistema para navegar.
    if (location.pathname.toLowerCase().startsWith('/login')) return;

    evento.preventDefault(); // o F3 do navegador abre a busca na pagina

    if (location.pathname === destino) return;

    if (window.Blazor && typeof window.Blazor.navigateTo === 'function') {
        window.Blazor.navigateTo(destino);
    } else {
        location.href = destino;
    }
});

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

// Mascara de digitacao: <input data-mascara="telefone|cpf|cnpj|cep|moeda">.
// Delegado no document porque o Blazor cria e recria os campos o tempo
// todo. So formata enquanto a pessoa digita; o @bind do Blazor le o valor
// no change (ao sair do campo), ja formatado. Quem confere se o valor vale
// e o servidor (Configs/Documentos.cs).
(function () {
    function digitos(texto, max) {
        return texto.replace(/\D/g, '').slice(0, max);
    }

    const mascaras = {
        telefone: function (v) {
            const d = digitos(v, 11);
            if (d.length <= 2) return d.length ? '(' + d : '';
            const meio = d.length === 11 ? 7 : 6;
            if (d.length <= meio) return '(' + d.slice(0, 2) + ') ' + d.slice(2);
            return '(' + d.slice(0, 2) + ') ' + d.slice(2, meio) + '-' + d.slice(meio);
        },
        cpf: function (v) {
            const d = digitos(v, 11);
            return d.replace(/^(\d{3})(\d)/, '$1.$2')
                    .replace(/^(\d{3})\.(\d{3})(\d)/, '$1.$2.$3')
                    .replace(/\.(\d{3})(\d)/, '.$1-$2');
        },
        cnpj: function (v) {
            const d = digitos(v, 14);
            return d.replace(/^(\d{2})(\d)/, '$1.$2')
                    .replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3')
                    .replace(/\.(\d{3})(\d)/, '.$1/$2')
                    .replace(/(\d{4})(\d)/, '$1-$2');
        },
        // Ate 11 digitos e CPF; passou disso, CNPJ.
        cpfcnpj: function (v) {
            return v.replace(/\D/g, '').length <= 11 ? mascaras.cpf(v) : mascaras.cnpj(v);
        },
        cep: function (v) {
            const d = digitos(v, 8);
            return d.length > 5 ? d.slice(0, 5) + '-' + d.slice(5) : d;
        },
        // Centavos a partir dos digitos: "1234" vira "12,34".
        moeda: function (v) {
            const d = digitos(v, 12).replace(/^0+(?=\d)/, '');
            if (!d) return '';
            const inteiro = (d.length > 2 ? d.slice(0, -2) : '0').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
            return inteiro + ',' + d.padStart(2, '0').slice(-2);
        }
    };

    document.addEventListener('input', function (evento) {
        const campo = evento.target;
        if (!(campo instanceof HTMLInputElement)) return;

        const mascara = mascaras[campo.dataset.mascara];
        if (!mascara) return;

        // Cursor no fim so quando ele ja estava no fim: editar no meio do
        // numero nao pode jogar o cursor para longe.
        const noFim = campo.selectionStart === campo.value.length;
        const novo = mascara(campo.value);
        if (novo === campo.value) return;

        campo.value = novo;
        if (noFim) campo.setSelectionRange(novo.length, novo.length);
    });
})();
