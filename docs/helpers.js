"use strict";

const {
  Paragraph, TextRun, Table, TableRow, TableCell,
  WidthType, BorderStyle, AlignmentType, ShadingType, VerticalAlign,
} = require("docx");

const FONT          = "Century Gothic";
const PT            = (n) => n * 2;
const COLOR_DARK    = "1F2D3D";
const COLOR_ALT     = "F2F4F7";
const COLOR_WHITE   = "FFFFFF";
const COLOR_ACCENT  = "2E6DB4";

const sp = (after, before = 0) => ({ spacing: { before, after } });

function h1(text) {
  return new Paragraph({
    children: [new TextRun({ text, font: FONT, size: PT(11), bold: true, color: COLOR_ACCENT })],
    ...sp(PT(3), PT(7)),
  });
}

function h2(text) {
  return new Paragraph({
    children: [new TextRun({ text, font: FONT, size: PT(10), bold: true, color: COLOR_ACCENT })],
    ...sp(PT(2), PT(5)),
  });
}

function body(text, opts = {}) {
  return new Paragraph({
    children: [new TextRun({ text, font: FONT, size: PT(9), ...opts })],
    ...sp(PT(3)),
  });
}

function bullet(text) {
  return new Paragraph({
    children: [new TextRun({ text, font: FONT, size: PT(9) })],
    bullet: { level: 0 },
    ...sp(PT(2)),
  });
}

function code(text) {
  return new Paragraph({
    children: [new TextRun({
      text, font: "Courier New", size: PT(8),
      shading: { type: ShadingType.CLEAR, fill: "F0F0F0" },
    })],
    ...sp(PT(3)),
    indent: { left: 360 },
  });
}

function empty() {
  return new Paragraph({ children: [new TextRun({ text: "" })], ...sp(PT(2)) });
}

function tableCell(text, { bg, color = "000000", bold: isBold = false, width } = {}) {
  const borders = ["top", "bottom", "left", "right"].reduce((acc, side) => {
    acc[side] = { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" };
    return acc;
  }, {});
  const props = {
    verticalAlign: VerticalAlign.CENTER,
    margins: { top: PT(2), bottom: PT(2), left: PT(4), right: PT(4) },
    borders,
  };
  if (bg)    props.shading = { type: ShadingType.CLEAR, fill: bg };
  if (width) props.width   = { size: width, type: WidthType.PERCENTAGE };
  return new TableCell({
    ...props,
    children: [new Paragraph({
      children: [new TextRun({ text, font: FONT, size: PT(8), bold: isBold, color })],
      alignment: AlignmentType.LEFT,
    })],
  });
}

function configTable(rows) {
  const COLS = [
    ["Champ (JSON)", 25], ["Type", 10], ["Défaut", 13], ["Description", 52],
  ];
  const header = new TableRow({
    tableHeader: true,
    children: COLS.map(([label, w]) =>
      tableCell(label, { bg: COLOR_DARK, color: COLOR_WHITE, bold: true, width: w })
    ),
  });
  const dataRows = rows.map((cells, i) =>
    new TableRow({
      children: cells.map((text, j) =>
        tableCell(text, { bg: i % 2 ? COLOR_ALT : COLOR_WHITE, width: COLS[j][1] })
      ),
    })
  );
  return new Table({ width: { size: 100, type: WidthType.PERCENTAGE }, rows: [header, ...dataRows] });
}

module.exports = { FONT, PT, COLOR_ACCENT, h1, h2, body, bullet, code, empty, configTable };
