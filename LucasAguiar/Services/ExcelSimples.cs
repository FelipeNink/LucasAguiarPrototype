using System.IO.Compression;
using System.Security;
using System.Text;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Gera um .xlsx de uma aba sem biblioteca externa: o formato e um zip de
    /// alguns XMLs. Numeros e datas vao como valor cru (e a planilha que
    /// formata), para a pessoa poder somar e filtrar.
    /// </summary>
    public static class ExcelSimples
    {
        public const string TipoXlsx =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        // Indices de estilo definidos no styles.xml abaixo.
        private const int EstiloCabecalho = 1, EstiloData = 2, EstiloDataHora = 3, EstiloDecimal = 4;

        public static byte[] Gerar(string aba, string[] cabecalho, IEnumerable<object?[]> linhas)
        {
            var dados = linhas.ToList();

            var larguras = cabecalho.Select(c => c.Length).ToArray();
            foreach (var linha in dados)
                for (var i = 0; i < linha.Length && i < larguras.Length; i++)
                    larguras[i] = Math.Max(larguras[i], Texto(linha[i]).Length);

            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sheet.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sheet.Append("<cols>");
            for (var i = 0; i < larguras.Length; i++)
                sheet.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Math.Clamp(larguras[i] + 3, 10, 50)}\" customWidth=\"1\"/>");
            sheet.Append("</cols><sheetData>");

            sheet.Append("<row r=\"1\">");
            for (var c = 0; c < cabecalho.Length; c++)
                sheet.Append($"<c r=\"{Coluna(c)}1\" s=\"{EstiloCabecalho}\" t=\"inlineStr\"><is><t>{Escapar(cabecalho[c])}</t></is></c>");
            sheet.Append("</row>");

            for (var l = 0; l < dados.Count; l++)
            {
                var r = l + 2;
                sheet.Append($"<row r=\"{r}\">");
                for (var c = 0; c < dados[l].Length; c++)
                    sheet.Append(Celula($"{Coluna(c)}{r}", dados[l][c]));
                sheet.Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");

            using var memoria = new MemoryStream();
            using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
            {
                Adicionar(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");

                Adicionar(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                Adicionar(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets><sheet name=\"{Escapar(NomeAba(aba))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

                Adicionar(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");

                Adicionar(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\\ hh:mm\"/></numFmts>" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"5\">" +
                    "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                    "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "</cellXfs></styleSheet>");

                Adicionar(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            }

            return memoria.ToArray();
        }

        public static string NomeArquivo(string nome) =>
            $"{nome}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        private static string Celula(string referencia, object? valor)
        {
            switch (valor)
            {
                case null:
                    return "";

                case DateTime data:
                    // Meia-noite exata vira so data; senao leva a hora junto.
                    var estilo = data.TimeOfDay == TimeSpan.Zero ? EstiloData : EstiloDataHora;
                    return $"<c r=\"{referencia}\" s=\"{estilo}\"><v>{data.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";

                case decimal or double or float:
                    return $"<c r=\"{referencia}\" s=\"{EstiloDecimal}\"><v>{Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture)}</v></c>";

                case int or long or short or byte:
                    return $"<c r=\"{referencia}\"><v>{Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture)}</v></c>";

                case bool b:
                    return $"<c r=\"{referencia}\" t=\"inlineStr\"><is><t>{(b ? "Sim" : "Não")}</t></is></c>";

                default:
                    return $"<c r=\"{referencia}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escapar(valor.ToString() ?? "")}</t></is></c>";
            }
        }

        private static string Texto(object? valor) => valor switch
        {
            null => "",
            DateTime => "00/00/0000 00:00",
            decimal or double or float => "0000000,00",
            _ => valor.ToString() ?? ""
        };

        /// <summary>A, B, ... Z, AA. As telas tem poucas colunas, mas nao custa.</summary>
        private static string Coluna(int indice)
        {
            var nome = "";
            for (var n = indice + 1; n > 0; n = (n - 1) / 26)
                nome = (char)('A' + (n - 1) % 26) + nome;
            return nome;
        }

        private static string NomeAba(string nome)
        {
            var limpo = new string(nome.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
            return limpo.Length > 31 ? limpo[..31] : (limpo.Length == 0 ? "Planilha" : limpo);
        }

        /// <summary>Escapa e descarta caracteres de controle, que o XML nao aceita.</summary>
        private static string Escapar(string texto)
        {
            var limpo = new string(texto.Where(c => c >= ' ' || c == '\t' || c == '\n' || c == '\r').ToArray());
            return SecurityElement.Escape(limpo) ?? "";
        }

        private static void Adicionar(ZipArchive zip, string caminho, string conteudo)
        {
            var entrada = zip.CreateEntry(caminho, CompressionLevel.Optimal);
            using var fluxo = entrada.Open();
            var bytes = new UTF8Encoding(false).GetBytes(conteudo);
            fluxo.Write(bytes, 0, bytes.Length);
        }
    }
}
