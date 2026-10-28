"""Gera um PDF de exemplo para o teste no emulador (nenhum documento real vai para o repositório)."""
import sys
from fpdf import FPDF

pdf = FPDF()
pdf.add_page()
pdf.set_font("Helvetica", "B", 16)
pdf.cell(0, 10, "Documento de teste do Envy Converter", new_x="LMARGIN", new_y="NEXT")
pdf.set_font("Helvetica", "", 11)
for i in range(1, 9):
    pdf.multi_cell(0, 6, f"{i}. Paragrafo de exemplo com texto suficiente para ocupar mais de uma linha "
                         "e verificar a reconstrucao de paragrafos, recuos e alinhamento justificado.")
    pdf.ln(2)
pdf.add_page()
pdf.set_font("Helvetica", "B", 13)
pdf.cell(0, 10, "Segunda pagina", new_x="LMARGIN", new_y="NEXT")
pdf.output(sys.argv[1])
