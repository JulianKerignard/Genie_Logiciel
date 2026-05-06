"use strict";

const path = require("path");
const fs   = require("fs");
const {
  Document, Packer, Paragraph, TextRun,
  PageNumber, Footer, AlignmentType, convertInchesToTwip,
} = require("docx");

const { FONT, PT, COLOR_ACCENT } = require("./helpers");
const section1 = require("./sections/section1");
const section2 = require("./sections/section2");
const section3 = require("./sections/section3");
const section4 = require("./sections/section4");
const section5 = require("./sections/section5");
const section6 = require("./sections/section6");

// ─── Page margin: 1.5 cm ──────────────────────────────────────────────────────
const MARGIN = convertInchesToTwip(0.59);

function header() {
  return [
    new Paragraph({
      children: [new TextRun({
        text: "EasySave V3.0 — Manuel Utilisateur",
        font: FONT, size: PT(14), bold: true, color: COLOR_ACCENT,
      })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(2) },
    }),
    new Paragraph({
      children: [new TextRun({
        text: "Version 3.0  ·  Mai 2026  ·  Samuel (Dev4)",
        font: FONT, size: PT(9), color: "555555",
      })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(8) },
    }),
  ];
}

function pageFooter() {
  return new Footer({
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      children: [
        new TextRun({ text: "EasySave V3.0  ·  Page ", font: FONT, size: PT(8), color: "888888" }),
        new TextRun({ children: [PageNumber.CURRENT], font: FONT, size: PT(8), color: "888888" }),
        new TextRun({ text: " / ", font: FONT, size: PT(8), color: "888888" }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], font: FONT, size: PT(8), color: "888888" }),
      ],
    })],
  });
}

async function main() {
  const doc = new Document({
    creator: "Samuel (Dev4)",
    title:   "EasySave V3.0 — Manuel Utilisateur",
    sections: [{
      properties: { page: { margin: { top: MARGIN, bottom: MARGIN, left: MARGIN, right: MARGIN } } },
      footers: { default: pageFooter() },
      children: [
        ...header(),
        ...section1(),
        ...section2(),
        ...section3(),
        ...section4(),
        ...section5(),
        ...section6(),
      ],
    }],
  });

  const outPath = path.join(__dirname, "ManuelUtilisateur_EasySave_V3.docx");
  fs.writeFileSync(outPath, await Packer.toBuffer(doc));
  console.log(`Document généré : ${outPath}`);
}

main().catch((err) => { console.error(err); process.exit(1); });
