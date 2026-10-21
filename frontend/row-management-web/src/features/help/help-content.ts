/**
 * The in-app Help center (ADR 0022). Plain data, so the page, its search and its tests all read one source.
 * Write what the app does today, in the words a land agent would use. Anything not built yet says so.
 */
export type HelpBlock =
  | { kind: 'p'; text: string }
  | { kind: 'steps'; title?: string; items: string[] }
  | { kind: 'list'; title?: string; items: string[] }
  | { kind: 'note'; text: string }
  | { kind: 'table'; head: string[]; rows: string[][] }
  /** Filled in at render time from the signed-in person's own access. */
  | { kind: 'my-access' }

export interface HelpTopic {
  id: string
  section: string
  title: string
  /** One line, shown in search results and under the title. */
  summary: string
  blocks: HelpBlock[]
}

export const HELP_SECTIONS = ['Getting started', 'Your dashboard', 'Projects and parcels', 'Acquisition work', 'Setting up', 'Tips and troubleshooting'] as const

export const HELP_TOPICS: HelpTopic[] = [
  // ── Getting started ──────────────────────────────────────────────────────
  {
    id: 'introduction', section: 'Getting started', title: 'Introduction to Right of Way Management',
    summary: 'What the application is for and who uses it.',
    blocks: [
      { kind: 'p', text: 'Masterworks Right of Way Management helps a transportation agency acquire the land a project needs. Each parcel has owners, a title and survey package, an appraisal that sets just compensation, a negotiation, and a closing. The application keeps all of it in one place, so everyone sees the same facts.' },
      { kind: 'p', text: 'Work is organised around projects and parcels. A project owns many parcels. Everything else (owners, title records, appraisals, negotiations, files) hangs off a parcel.' },
      { kind: 'table', head: ['If you are a…', 'You mostly work in…'], rows: [
        ['Project Manager', 'Projects, Parcels, Requests, and the Team view on the dashboard'],
        ['Designer or Design Approver', 'Projects and Parcels'],
        ['Surveyor', 'Parcel Location and Easements'],
        ['Title Researcher', 'Title & Survey, Title Encumbrances, Title and Closing'],
        ['Appraiser or Appraisal Approver', 'Appraisal'],
        ['Negotiator', 'Negotiation & Closing, Contact Log, Ownership interests'],
        ['Relocation Agent', 'Questionnaire and Relocation'],
        ['Finance', 'Requests and the records you read for payment'],
        ['Administrator', 'Everything, plus Settings, Library and Configuration'],
      ] },
      { kind: 'note', text: 'This is the first version. It covers straight-through acquisition: setting up a project and parcels, title and survey, appraisal and just compensation, negotiation, agreement and closing.' },
    ],
  },
  {
    id: 'key-concepts', section: 'Getting started', title: 'Key concepts',
    summary: 'The handful of words the rest of the application uses.',
    blocks: [
      { kind: 'table', head: ['Term', 'What it means'], rows: [
        ['Project', 'A transportation project that needs land. It has a number the application assigns, a status and an oversight type (Federal, State or Local).'],
        ['Parcel', 'One piece of land a project needs. It has a number, an address, an owner, a status and a set of tabs.'],
        ['Parcel status', 'Where a parcel stands. Your agency chooses the list, and a person sets the status by hand.'],
        ['Just compensation', 'The amount an approved appraisal says the owner is owed. Only an Appraisal Approver or an Administrator can record the approved figure.'],
        ['Interest', 'A person or company that holds a right in the parcel (owner, tenant, lienholder).'],
        ['Right of entry', 'Permission to go onto the land before it is acquired.'],
        ['Encumbrance', 'Something on the title that limits the land, such as a lien or an easement.'],
        ['Role', 'What you are allowed to do. Your agency can add roles; the Administrator decides which pages each role can see and change.'],
      ] },
    ],
  },
  {
    id: 'quick-start', section: 'Getting started', title: 'Quick start',
    summary: 'Sign in, find a parcel, and add your first record in a few minutes.',
    blocks: [
      { kind: 'steps', items: [
        'Sign in with your user name and password. In a demo, pick your role on the sign-in page.',
        'Look at the Dashboard. It shows what is waiting for you and what you have done.',
        'Open Parcels in the left menu and search by parcel number, address or owner name.',
        'Open a parcel. The tabs across the top hold its owners, title records, appraisals, negotiation and more.',
        'Pick the tab for your job and use Add. Fields marked with a star are required. Save when you are done.',
        'Use the question mark at the top right whenever you want to come back to this help.',
      ] },
      { kind: 'note', text: 'You only see the menu items and tabs your role can open. If something you expect is missing, ask your Administrator to check your role.' },
    ],
  },
  {
    id: 'my-access', section: 'Getting started', title: 'What your role can do',
    summary: 'The pages your sign-in can open, listed from your own access.',
    blocks: [
      { kind: 'p', text: 'This list is built from your own permissions, so it is always right for you. "Read" means you can view the page. "Change" means you can add or update records on it.' },
      { kind: 'my-access' },
      { kind: 'p', text: 'Some rules sit on top of page access. Appraisal review fields can only be set by an Appraisal Approver or an Administrator. Only an Administrator can remove a Title and Closing record. Nobody can approve a request they entered themselves.' },
    ],
  },

  // ── Dashboard ────────────────────────────────────────────────────────────
  {
    id: 'dashboard-my-work', section: 'Your dashboard', title: 'Your dashboard: what is waiting and what you did',
    summary: 'A page of your own, built from the records you can open.',
    blocks: [
      { kind: 'p', text: 'The Dashboard is different for every person. The top row shows how many things need your attention, how many parcel deadlines are overdue, how many things you did in the last 30 days, and when you were last active.' },
      { kind: 'list', title: 'The cards', items: [
        'Waiting for you lists the work your role can open, with a count for each line. Click a line to go to the screen where you can do it. Lines with nothing behind them are hidden.',
        'What you have done is a seven-day chart of the records you added, changed or removed.',
        'Your work by record shows the same thing for the last 30 days, grouped by kind of record.',
        'Your recent activity lists the latest things you did.',
        'Next actions lists parcel deadlines that are overdue or due within seven days.',
      ] },
      { kind: 'note', text: 'One save that changes several fields on a record counts once, not once per field. Sign-in tokens and saved filters are never counted.' },
      { kind: 'p', text: 'Use Refresh at the top right to update the numbers. They come straight from the records, so they are as current as your last refresh.' },
    ],
  },
  {
    id: 'dashboard-program', section: 'Your dashboard', title: 'The Program view for a ROW Director',
    summary: 'Where right of way stands on every project and parcel, and which teams take the time.',
    blocks: [
      { kind: 'p', text: 'Project Managers and Administrators land on the Program tab. It answers: where does right of way stand on each project, what is going on with each parcel, and where is the time going?' },
      { kind: 'list', title: 'What you see', items: [
        'Tiles for projects, active parcels, the share closed, projects needing attention, parcels stuck on a step, the estimated ROW cost and the known cost so far.',
        'A table of projects with a health label and the first reason, how much is closed, a bar showing where the parcels are in the workflow, overdue and stuck parcels, the estimate, the known cost and the ROW clear date.',
        'Where the time goes: teams ranked by the average days a parcel stays with them, longest first, and a table of every workflow step.',
        'The parcels that have been on one step the longest, each linking to the parcel.',
      ] },
      { kind: 'steps', title: 'Look at one project', items: [
        'Choose the project number in the table.',
        'Read the reasons under the title. They say exactly why a project is Watch or Off track.',
        'Scan the parcel table: owner, status, step and owning team, days on the step, appraisal, approved just compensation, agreed amount, closing date and anything needing attention.',
        'Choose a parcel number to open the parcel. Choose All projects to go back.',
      ] },
      { kind: 'table', head: ['Health', 'When'], rows: [
        ['Off track', 'The ROW clear date has passed with parcels not closed; or the known cost is over the estimate; or at least three parcels, and a quarter of the project, have an overdue deadline.'],
        ['Watch', 'Any overdue deadline; or the ROW clear date is within 90 days with under 75% closed; or a parcel has been on one step for over 30 days.'],
        ['On track', 'None of the above.'],
      ] },
      { kind: 'note', text: 'Time in a step is counted from the moves recorded on each parcel, so move parcels along as work finishes. Known cost uses the agreed amount, or the approved just compensation where nothing is agreed yet.' },
    ],
  },
  {
    id: 'dashboard-team', section: 'Your dashboard', title: 'The Team view for managers',
    summary: 'Project Managers and Administrators see everyone’s work added together.',
    blocks: [
      { kind: 'p', text: 'If you are a Project Manager or an Administrator, the Dashboard has three tabs: Program, My work and Team. Team rolls up what every person did.' },
      { kind: 'list', items: [
        'Tiles for projects, active parcels, requests and reports awaiting a decision, overdue deadlines, rights of entry about to expire, and closings due. Click a tile to open the matching screen.',
        'Parcels by status, in the order your agency set up.',
        'A seven-day chart and a by-record table for the whole team.',
        'A People table: for each person, what they added, changed and removed in the last 30 days, when they were last active, and how many requests and draft reports they have open.',
      ] },
      { kind: 'note', text: 'Everyone else is refused the Team view. Their own My work page is unaffected.' },
    ],
  },

  // ── Projects and parcels ─────────────────────────────────────────────────
  {
    id: 'projects', section: 'Projects and parcels', title: 'Projects',
    summary: 'Create a project, set its oversight type and add parcels to it.',
    blocks: [
      { kind: 'steps', title: 'Create a project', items: [
        'Open Projects and choose Create.',
        'Give it a name and choose the oversight type (Federal, State or Local).',
        'Save. The application assigns the project number.',
      ] },
      { kind: 'p', text: 'Open a project to see its details, parcels, funding and the Planned/Build link. Federal and State projects are gated: parcels can be acquired only once the environmental decision and the acquisition authorization are recorded, unless an exception is noted.' },
      { kind: 'note', text: 'Each project has its own time zone, and deadlines are counted in the project’s time zone, not yours.' },
    ],
  },
  {
    id: 'parcels', section: 'Projects and parcels', title: 'Parcels and the parcel board',
    summary: 'Find a parcel, change its status, split or combine it.',
    blocks: [
      { kind: 'p', text: 'The Parcels page lists every parcel. Search by parcel number, address or owner name, and filter by project or status. Save a filter you use often and it will be there next time.' },
      { kind: 'list', title: 'What you can do on a parcel', items: [
        'Change its status from your agency’s list.',
        'Split it into two parcels or combine several into one. The history stays on record.',
        'Record milestones, site visits and the notice to owner. Statutory deadlines are worked out from the milestones.',
        'Assign the parcel team (pick an agent type and a person from the Library) and write notes and diary entries.',
      ] },
      { kind: 'note', text: 'Where a step must happen before another, the application stops you and says why. An Administrator can allow certain roles to override.' },
    ],
  },
  {
    id: 'parcel-tabs', section: 'Projects and parcels', title: 'The parcel tabs',
    summary: 'Every tab inside a parcel, who edits it, and what it holds.',
    blocks: [
      { kind: 'p', text: 'Each tab has its own access, so a person might read every tab but only change the ones for their job. Every tab can have files attached.' },
      { kind: 'table', head: ['Tab', 'What it holds', 'Usually edited by'], rows: [
        ['Ownership interests', 'Owners and interest holders with owner type, addresses and contacts in one form. The parcel’s owner name comes from here.', 'Designer, Negotiator'],
        ['Parcel location', 'CAD number and map save status (read-only).', 'Designer, Surveyor'],
        ['Title encumbrances', 'Liens, easements and other limits on the title, with a cure status.', 'Title Researcher'],
        ['Title and closing', 'Conveyance documents, title commitments, closing dates and the vested owner.', 'Title Researcher, Negotiator'],
        ['Appraisal', 'Appraisals with their amounts. Review fields and approved just compensation are for approvers.', 'Appraiser, Appraisal Approver'],
        ['Negotiation', 'Offers, settlement, counteroffers, the right of entry form and status notes.', 'Negotiator'],
        ['Questionnaire', 'Ownership interests and question and answer grids.', 'Relocation Agent'],
        ['Condemnation, Property management and Relocation', 'Records your agency fills in. The processes behind them come later.', 'Negotiator, Project Manager, Relocation Agent'],
        ['Contact log', 'Every call, email and visit with the owner.', 'Negotiator'],
        ['Easements', 'Easements with station range and term.', 'Surveyor'],
      ] },
    ],
  },
  {
    id: 'parties', section: 'Projects and parcels', title: 'Parties and owners',
    summary: 'Enter a person or company once and link them to many parcels.',
    blocks: [
      { kind: 'p', text: 'A party is a person or company. Enter them once on the Parties page, then link them to a parcel or tract in a role, such as owner or tenant. Owners recorded on a tract are inherited by its parcels.' },
      { kind: 'note', text: 'Tax IDs can be typed in but are never shown again. They are stored encrypted and appear masked everywhere.' },
    ],
  },

  // ── Acquisition work ─────────────────────────────────────────────────────
  {
    id: 'requests', section: 'Acquisition work', title: 'Requests and approvals',
    summary: 'Ask for a property to be acquired and have somebody else approve it.',
    blocks: [
      { kind: 'steps', title: 'Submit a request', items: [
        'Open Requests and start a new request.',
        'Say who is asking, which property, and the estimated amount.',
        'Submit. It now waits for a decision.',
      ] },
      { kind: 'p', text: 'People whose role can approve see it under Waiting for you on their Dashboard. They approve it, or reject it with a reason. You cannot decide a request you entered yourself.' },
    ],
  },
  {
    id: 'work-screens', section: 'Acquisition work', title: 'Title & Survey, Appraisal, and Negotiation & Closing',
    summary: 'Search across every parcel, open one, and work on it right there.',
    blocks: [
      { kind: 'steps', items: [
        'Open the screen from the left menu.',
        'Type a parcel number, address, owner name or a record ID such as APR-1001-001 into Search.',
        'Choose Open next to a record or a parcel. The parcel’s own tab opens below the results.',
        'Add, update or remove the record there. The tab applies your real permissions.',
        'Choose Close parcel to go back to the search.',
      ] },
      { kind: 'note', text: 'With no search text, you see the most recent records. A search returns at most 200 rows. Narrow it if you do not see what you want.' },
    ],
  },
  {
    id: 'rights-of-entry', section: 'Acquisition work', title: 'Rights of entry',
    summary: 'Track permission to enter land and see which ones are about to expire.',
    blocks: [
      { kind: 'p', text: 'Record the right of entry in the parcel’s Negotiation tab (the only place it is entered), with who granted it and when it ends. The Rights of entry report lists them across projects. Granted rights that end within 30 days are counted on your Dashboard.' },
    ],
  },

  // ── Setting up ───────────────────────────────────────────────────────────
  {
    id: 'settings-roles', section: 'Setting up', title: 'Settings: users, roles and access',
    summary: 'For Administrators: who can do what.',
    blocks: [
      { kind: 'p', text: 'Settings lists the users and roles. The permission grid has one row per page and one column per role, with Create, Read, Update and Delete for each. Add a role when your agency has a job the default roles do not fit.' },
      { kind: 'note', text: 'Default roles cannot be renamed or deleted, but their access can be edited.' },
    ],
  },
  {
    id: 'library', section: 'Setting up', title: 'The Library',
    summary: 'The lists that fill the dropdowns across the application.',
    blocks: [
      { kind: 'p', text: 'The Library holds the choices you pick from in forms, such as parcel statuses, appraisal types and cure statuses. Everyone can read it. Administrators add and change values. The Agents list holds the people who can be assigned to parcels.' },
    ],
  },
  {
    id: 'configuration', section: 'Setting up', title: 'Configuration',
    summary: 'Parcel statuses, groups, workflow steps and deadline rules.',
    blocks: [
      { kind: 'p', text: 'Configuration is for Administrators. Set the parcel statuses and groups, the order of workflow steps and who may override a step, and the statutory deadline rules that drive reminders.' },
    ],
  },

  // ── Tips ─────────────────────────────────────────────────────────────────
  {
    id: 'searching', section: 'Tips and troubleshooting', title: 'Searching and saving filters',
    summary: 'Find things quickly and keep the filters you use.',
    blocks: [
      { kind: 'list', items: [
        'Search matches parcel numbers, addresses and owner names without caring about capital letters.',
        'Record IDs follow a pattern: APR (appraisal), NEG (negotiation), TC (title and closing), ENC (encumbrance), CL (contact log), QST (questionnaire).',
        'A saved filter belongs to you and to the page you saved it on.',
      ] },
    ],
  },
  {
    id: 'attachments', section: 'Tips and troubleshooting', title: 'Attaching files',
    summary: 'Add documents to a record.',
    blocks: [
      { kind: 'p', text: 'Any record that shows an Attachments section accepts files. Choose a file and upload. Removing a file hides it but keeps the audit trail.' },
    ],
  },
  {
    id: 'accessibility', section: 'Tips and troubleshooting', title: 'Keyboard and accessibility',
    summary: 'Use the application without a mouse.',
    blocks: [
      { kind: 'list', items: [
        'Tab and Shift+Tab move between controls. Enter or Space presses a button.',
        'The left menu collapses to icons with the toggle at its top. Every icon keeps its name for screen readers.',
        'Charts have a text summary that a screen reader reads aloud.',
        'The layout works down to tablet width without sideways scrolling.',
      ] },
    ],
  },
  {
    id: 'troubleshooting', section: 'Tips and troubleshooting', title: 'Troubleshooting',
    summary: 'The messages you are most likely to meet, and what to do.',
    blocks: [
      { kind: 'table', head: ['What you see', 'What to do'], rows: [
        ['You don’t have access', 'Your role cannot open that page. Ask your Administrator.'],
        ['Your session has expired', 'Sign in again. You will land back where you were.'],
        ['Cannot reach the server', 'Check your connection and try again.'],
        ['Somebody else changed this record', 'Reload, review the new values and make your change again.'],
        ['A menu item or tab is missing', 'It is hidden when your role cannot read it.'],
        ['The dashboard shows nothing', 'You may not have done anything yet. Add a record on a parcel and press Refresh.'],
      ] },
    ],
  },
]

export const topicById = (id: string | undefined) => HELP_TOPICS.find((t) => t.id === id)

const flat = (t: HelpTopic) => [t.title, t.summary, ...t.blocks.flatMap((b) => {
  switch (b.kind) {
    case 'p': case 'note': return [b.text]
    case 'steps': case 'list': return [b.title ?? '', ...b.items]
    case 'table': return [...b.head, ...b.rows.flat()]
    default: return []
  }
})].join(' ').toLowerCase()

/** Topics whose title, summary or text contain every word typed; title matches first. No text lists every topic. */
export function searchTopics(text: string): HelpTopic[] {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return HELP_TOPICS
  return HELP_TOPICS
    .filter((t) => { const hay = flat(t); return words.every((w) => hay.includes(w)) })
    .sort((a, b) => Number(words.every((w) => b.title.toLowerCase().includes(w))) - Number(words.every((w) => a.title.toLowerCase().includes(w))))
}
