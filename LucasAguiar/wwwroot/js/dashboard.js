// Graficos da tela inicial.
// Carregado no <head> do App.razor: o Blazor usa navegacao aprimorada e nao
// re-executa <script> declarado dentro de uma pagina, entao o bootstrap precisa
// viver aqui fora e reagir ao evento 'enhancedload'.
(function () {
    // As cores saem dos tokens do CSS, nao ficam escritas aqui: assim o
    // grafico acompanha a troca de tema em vez de manter grade clara e
    // rotulo escuro sobre fundo escuro.
    function token(nome, reserva) {
        var v = getComputedStyle(document.documentElement)
            .getPropertyValue(nome).trim();
        return v || reserva;
    }

    function paletaAtual() {
        return {
            laranja: token('--marca', '#f0651a'),
            laranjaClaro: token('--marca-clara', '#fe8413'),
            vermelho: token('--perigo-escuro', '#c9302c'),
            texto: token('--texto-3', '#6b7280'),
            grade: token('--borda', '#eceff3'),
            superficie: token('--superficie', '#fff'),
            // Balao invertido em relacao a pagina: escuro no tema claro,
            // claro no escuro. Legivel nos dois sem cor propria.
            balao: token('--texto', '#1f2937'),
            balaoTexto: token('--superficie', '#fff'),
            vazio: token('--borda-suave', '#e5e7eb')
        };
    }

    function coresDasFormas(p) {
        return [p.laranja, token('--ok', '#16a34a'), '#3b82f6',
                token('--roxo', '#a855f7'), token('--borda-3', '#d1d5db')];
    }

    let graficoBarras = null;
    let graficoPizza = null;

    /// Le os dados que o Home.razor deixou no elemento oculto.
    function lerDados() {
        const alvo = document.getElementById('dados-painel');
        if (!alvo) return null;
        try {
            return JSON.parse(alvo.textContent || '{}');
        } catch (e) {
            return null;
        }
    }

    function moeda(v) {
        return 'R$ ' + Number(v).toLocaleString('pt-BR', { minimumFractionDigits: 2 });
    }

    function desenhar() {
        if (typeof Chart === 'undefined') return;

        const alvoBarras = document.getElementById('barChart');
        const alvoPizza = document.getElementById('pieChart');

        if (!alvoBarras || !alvoPizza) {
            if (graficoBarras) { graficoBarras.destroy(); graficoBarras = null; }
            if (graficoPizza) { graficoPizza.destroy(); graficoPizza = null; }
            return;
        }

        const paleta = paletaAtual();
        const dados = lerDados();
        if (!dados || !dados.meses) return;

        const existenteBarras = Chart.getChart(alvoBarras);
        if (existenteBarras) existenteBarras.destroy();
        const existentePizza = Chart.getChart(alvoPizza);
        if (existentePizza) existentePizza.destroy();

        Chart.defaults.font.family = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";
        Chart.defaults.font.size = 12;
        Chart.defaults.color = paleta.texto;

        graficoBarras = new Chart(alvoBarras, {
            type: 'bar',
            data: {
                labels: dados.meses,
                datasets: [
                    {
                        label: 'Receita',
                        data: dados.receitas,
                        backgroundColor: paleta.laranja,
                        hoverBackgroundColor: paleta.laranjaClaro,
                        borderRadius: 4,
                        borderSkipped: false,
                        maxBarThickness: 26
                    },
                    {
                        label: 'Despesas',
                        data: dados.despesas,
                        backgroundColor: paleta.vermelho,
                        borderRadius: 4,
                        borderSkipped: false,
                        maxBarThickness: 26
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'top',
                        align: 'end',
                        labels: { usePointStyle: true, pointStyle: 'circle', boxWidth: 8, padding: 14 }
                    },
                    tooltip: {
                        backgroundColor: paleta.balao,
                        titleColor: paleta.balaoTexto,
                        bodyColor: paleta.balaoTexto,
                        padding: 10,
                        callbacks: {
                            label: function (ctx) {
                                return ' ' + ctx.dataset.label + ': ' + moeda(ctx.parsed.y);
                            }
                        }
                    }
                },
                scales: {
                    x: { grid: { display: false }, border: { display: false } },
                    y: {
                        beginAtZero: true,
                        border: { display: false },
                        grid: { color: paleta.grade },
                        ticks: {
                            callback: function (v) {
                                return v >= 1000 ? 'R$ ' + (v / 1000) + 'k' : 'R$ ' + v;
                            }
                        }
                    }
                }
            }
        });

        const temFormas = dados.formasValores && dados.formasValores.length > 0;

        graficoPizza = new Chart(alvoPizza, {
            type: 'doughnut',
            data: {
                labels: temFormas ? dados.formasRotulos : ['Sem vendas no período'],
                datasets: [{
                    data: temFormas ? dados.formasValores : [1],
                    backgroundColor: temFormas ? coresDasFormas(paleta) : [paleta.vazio],
                    borderColor: paleta.superficie,
                    borderWidth: 2,
                    hoverOffset: temFormas ? 6 : 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '58%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { usePointStyle: true, pointStyle: 'circle', padding: 14, boxWidth: 8 }
                    },
                    tooltip: {
                        enabled: temFormas,
                        backgroundColor: paleta.balao,
                        titleColor: paleta.balaoTexto,
                        bodyColor: paleta.balaoTexto,
                        padding: 10,
                        callbacks: {
                            label: function (ctx) {
                                return ' ' + ctx.label + ': ' + moeda(ctx.parsed);
                            }
                        }
                    }
                }
            }
        });

        // O layout so esta resolvido no proximo frame.
        requestAnimationFrame(function () {
            if (graficoBarras) graficoBarras.resize();
            if (graficoPizza) graficoPizza.resize();
        });
    }

    function agendar() {
        requestAnimationFrame(desenhar);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', agendar);
    } else {
        agendar();
    }

    window.addEventListener('load', agendar);

    // Navegacao aprimorada do Blazor troca o DOM sem recarregar a pagina.
    // O objeto Blazor so existe depois que blazor.web.js inicializa.
    function registrarEnhancedLoad() {
        if (window.Blazor && typeof Blazor.addEventListener === 'function') {
            Blazor.addEventListener('enhancedload', agendar);
            return true;
        }
        return false;
    }

    if (!registrarEnhancedLoad()) {
        const tentativa = setInterval(function () {
            if (registrarEnhancedLoad()) clearInterval(tentativa);
        }, 100);
        setTimeout(function () { clearInterval(tentativa); }, 15000);
    }

    // Rede de seguranca: ao iniciar o circuito interativo o Blazor reconcilia o
    // DOM e troca os elementos <canvas>, descartando os graficos ja montados.
    // Tambem cobre a troca de periodo, que redesenha os dados sem recarregar.
    let assinaturaAnterior = '';
    setInterval(function () {
        if (typeof Chart === 'undefined') return;

        const alvo = document.getElementById('barChart');
        if (!alvo) return;

        const dados = document.getElementById('dados-painel');
        // O tema entra na assinatura: trocar de claro para escuro muda as
        // cores da grade e dos rotulos, entao o grafico precisa ser refeito.
        const assinatura = (dados ? dados.textContent : "")
            + "|" + document.documentElement.getAttribute("data-tema");

        if (!Chart.getChart(alvo) || assinatura !== assinaturaAnterior) {
            assinaturaAnterior = assinatura;
            desenhar();
        }
    }, 500);
})();
