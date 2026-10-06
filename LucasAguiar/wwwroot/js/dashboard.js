// Graficos da tela inicial.
// Carregado no <head> do App.razor: o Blazor usa navegacao aprimorada e nao
// re-executa <script> declarado dentro de uma pagina, entao o bootstrap precisa
// viver aqui fora e reagir ao evento 'enhancedload'.
(function () {
    const paleta = {
        laranja: '#f0651a',
        laranjaClaro: '#fe8413',
        vermelho: '#c9302c',
        texto: '#6b7280',
        grade: '#eceff3'
    };

    const coresFormas = ['#f0651a', '#16a34a', '#3b82f6', '#a855f7', '#d1d5db'];

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
                        backgroundColor: '#1f2937',
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
                    backgroundColor: temFormas ? coresFormas : ['#e5e7eb'],
                    borderColor: '#fff',
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
                        backgroundColor: '#1f2937',
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
        const assinatura = dados ? dados.textContent : '';

        if (!Chart.getChart(alvo) || assinatura !== assinaturaAnterior) {
            assinaturaAnterior = assinatura;
            desenhar();
        }
    }, 500);
})();
