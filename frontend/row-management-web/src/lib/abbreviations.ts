/**
 * What the abbreviations on screen stand for, so a tooltip can explain them. Terms the source requirements never spell out (JAT, PDB)
 * say so instead of guessing; they are the agency's own words. Add a term here and every label, heading and column that uses it explains it.
 */
export const ABBREVIATIONS: Readonly<Record<string, string>> = {
  ROW: 'Right-of-way: the land a transportation project needs to build on.',
  URA: 'Uniform Relocation Assistance and Real Property Acquisition Policies Act (the "Uniform Act"): the federal rules for buying land and helping displaced people.',
  PUA: 'Possession and Use Agreement: lets the agency take possession of the property before the sale closes.',
  JAT: 'An agency review body that counteroffers are submitted to. The expansion is not given in the source requirements; confirm it with your agency.',
  ATP: 'Austin Transit Partnership: the agency whose approvals and sign-offs these dates track.',
  PDB: 'A property-disposition office or database that transfer memos are sent to. The expansion is not given in the source requirements; confirm it with your agency.',
  NOH: 'Notice of hearing: the notice that a condemnation hearing has been set.',
  NOD: 'Notice of Deposit: the agency has deposited the offered amount with the court and may take possession.',
  GIN: 'General Information Notice: the first written notice a displaced person gets, explaining relocation help (URA).',
  NEPA: 'National Environmental Policy Act: the federal environmental review a project needs before federal-aid acquisition.',
  FTA: 'Federal Transit Administration.',
  FHWA: 'Federal Highway Administration.',
  CAD: 'Central Appraisal District: the county office that keeps the tax appraisal record and account number for the parcel.',
  ESRI: 'The GIS mapping software vendor; this shows whether the parcel record was saved to the agency\'s ESRI map.',
  GIS: 'Geographic information system (digital maps).',
  GF: 'Guaranty File number: the title company\'s file number for this transaction.',
  'W-9': 'IRS Form W-9: the owner\'s tax identification form. The number is stored encrypted and never shown in full.',
  'UCC-1': 'Uniform Commercial Code financing statement: a public filing of a lender\'s claim on personal property.',
  APN: 'Assessor\'s parcel number: the tax office\'s identifier for the parcel.',
  LEP: 'Limited English proficiency: the person needs notices and help in another language.',
  DOT: 'Department of Transportation.',
  FY: 'Fiscal year.',
  KPI: 'Key performance indicator.',
  EFT: 'Electronic funds transfer.',
  SSN: 'Social Security number.',
  EIN: 'Employer identification number.',
  LLC: 'Limited liability company.',
  PM: 'Project manager.',
}

const terms = Object.keys(ABBREVIATIONS).sort((a, b) => b.length - a.length)// An abbreviation is a whole upper-case word: "ROW-014" (a requirement ID), "PN-1001" or "ROWS" do not count.
const PATTERN = new RegExp(String.raw`(?<![\w-])(${terms.join('|')})(?![\w-])`, 'g')

/** Cuts a piece of text into plain runs and known abbreviations, in order. */
export function splitAbbreviations(text: string): { text: string; term?: string }[] {
  const out: { text: string; term?: string }[] = []
  let last = 0
  for (const m of text.matchAll(PATTERN)) {
    const at = m.index ?? 0
    if (at > last) out.push({ text: text.slice(last, at) })
    out.push({ text: m[0], term: m[0] })
    last = at + m[0].length
  }
  if (last < text.length) out.push({ text: text.slice(last) })
  return out
}
