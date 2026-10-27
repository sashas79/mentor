# Mentorship App - Business Requirements Specification (BRS)

**Document ID:** DAR-MENT-BRS-0001  |  **Version:** v0.3.0  |  **Prepared:** 20 October 2026  |  **Status:** Candidate — business approval outstanding  |  **Business owner:** Talent Management

**Purpose.** Define Dariel’s first viable mentorship application and a working end-to-end demonstrator. This revision reconciles the prior BRS v0.2 and Engineering Harness YAML v0.2.1, removes re-opened scope questions already settled by the product owner, and includes the agreed pre-formation draft reset rule. Candidate status indicates formal BRS sign-off has not yet occurred; it does **not** reopen settled functional decisions.

## Document control and evidence
| Field | Value |
|---|---|
| Authority | Requirements represent the agreed candidate scope; explicit production policy gaps are recorded separately. |
| Source basis | Earlier BRS v0.2 and Engineering Harness specification v0.2.1; original form, agreement, tracker and discovery context. |
| Architecture boundary | Business behaviour is specified here; tables/routes/UI structures remain architecture and design decisions. |
| Baseline | No formal Talent Management sign-off or approved Harness baseline is asserted. |
| Downstream change | Do not continue using YAML v0.2.1 as if it reflects v0.3.0; re-ingest after review. |

| Ref | Evidence | Status |
|---|---|---|
| SRC-01 | Mentorship app_agent requirement gathering.docx — 14 Oct 2026 discovery | Referenced by the earlier BRS; project resource. |
| SRC-02 | Mentorship Form.pdf — initial mentorship request | Supplied source document. |
| SRC-03 | MENTORSHIP AGREEMENT.pdf — existing 5-page agreement | Supplied source document. |
| SRC-04 | Mentorship Tracker - 2026.xlsx — historical relationship register | Supplied source document. |
| SRC-05 | User-confirmed project decisions, 14–15 Oct 2026; documented in BRS v0.2 | Previously recorded decisions; not a separate attached document. |
| SRC-06 | User instruction dated 20 Oct 2026 — remove historical APR-001 and define draft reset exception | Current change authority. |

**Authority and evidence note:** SRC-05 is defined in the earlier BRS evidence register; the YAML’s claim that SRC-05 was absent from that register was incorrect. The complete original decision transcript is not embedded in this BRS. SRC-06 records the current explicitly provided instruction. No external approvals are invented.

## 1. Business context and objectives
Dariel currently handles mentorship requests, facilitation, formal agreement signing and relationship tracking through forms, human coordination and a spreadsheet. The product provides employee self-service, mentor discovery, traceable formation, structured goals/progress and authorised Talent Management reporting. It does not automate mentoring conversations, promotion outcomes or performance management.

Desired business outcomes, not yet numerically baselined: improved visibility of approved mentors; reduced administrative handling; auditable acceptance/approval; visible goal progress; consistent relationship register and history. Metrics are a separate business decision.

## 2. Product scope and boundaries
### 2.1 Included in MVP
- Microsoft Entra-backed identity in production, role-sensitive employee-maintained profiles and server-authorised actions.
- Mentor application, Talent Management approval and discovery of **all** approved mentors without capacity-based suppression.
- Protégé request capturing motivation, technical/career goals and nontechnical expectations, and independently recorded pairing choices.
- Digital agreement preparation; immutable per-acceptance content snapshots; pre-formation draft replacement/reset; two current participant acceptances; Talent Management formation approval.
- Fixed development goals in formed agreements; protégé progress and completion requests; mentor approval/rejection of completion.
- Independent `State`, `Status` and `IsActive` for formed relationships with auditable administrator edits.
- Representation of planned meetings, material workflow events/notification outcomes and authoritative reporting.
- Manual, validated legacy entry with identity resolution and provenance, without fabricated lifecycle events.
- End-to-end demonstrator with role-appropriate React UI and .NET/PostgreSQL services.

### 2.2 Explicit exclusions
- **Post-formation agreement amendments:** no change of signed terms, named parties, agreed goals or agreed mentor inside an existing formed relationship. A different agreement/mentor requires a new request and relationship.
- In-place mentor reassignment, goal carry-forward, goal wording amendment and associated approval workflows.
- AI ranking/recommendations/automated matching, numeric capacity restriction, and automatic approved-mentor unavailability suppression.
- Live calendar synchronisation, availability search, production rescheduling management or automatically enforced meeting commitments.
- Automated legacy import, live profile/Data Lake dependencies and private meeting discussion notes.
- Automatic restrictions or recalculation of unusual formed-relationship `State` / `Status` / `IsActive` combinations.
- Automatic goal completion escalation, mandated rejection reasons and resubmission loops.
- Unapproved general access to mentorship details and performance-management/promotion integration.

**Not excluded:** a substantive **pre-formation** draft revision is allowed under a full Step 1 reset. It is not a mutation of accepted snapshot content or an amendment to a formed relationship.

### 2.3 Demonstrator and production boundary
The first demonstrator must cover the three roles and the principal formation, draft-revision, goal, administration and reporting flows. Labelled simulated identities and simulated notification outcomes are acceptable if production integrations are unavailable. Server-side authorisation still applies. Entra integration, security, data protection and operating-readiness targets must be demonstrated or decided before **production** acceptance; a prototype does not satisfy them.

### 2.4 Delivery allocation (context, not an API specification)
The .NET backend and PostgreSQL authoritative store own data integrity, business rules, identity association, permission checks, workflow states and audits. A separately delivered React frontend created using Vercel v0 consumes those APIs and owns user interaction. Frontend implementation cannot substitute for backend rule enforcement. API routes, schema design, deployment and visual component structure are architecture decisions.

## 3. Actors and authority
| Actor | Responsibilities | Access boundary |
|---|---|---|
| Employee / prospective protégé | Authenticate; maintain own profile; browse mentors; submit mentorship request | Only own data and intentionally shared discoverable mentor data. |
| Protégé | Indicate willingness; accept current agreement draft; submit progress and completion request | Only relationships and candidate records in which they participate. |
| Mentor applicant | Submit/edit mentor biography/attributes and application | Not discoverable until Talent Management approval. |
| Approved mentor | Respond to invitation; accept current draft; approve/reject goal completion | Not a Talent Management administrator. |
| Talent Management administrator | Approve mentor registration and formation; edit formed relationship attributes; view structured goals/progress/register | Cannot view private discussion content; draft/approval actions are audited. |
| IT / system operator | Manage Entra, environment, backup, database and application operations | No automatic business-record permissions from technical operator access. |

**Permission-design obligation:** architecture must produce a server-side actor/resource/action matrix for draft revisions, candidate visibility, withdrawn requests, acceptance, audit, goals and reports; unspecified permissions must not default to broad access.

## 4. End-to-end business workflows
### WF-01 — Mentor onboarding and discovery
Employee registers → Talent Management approves or declines → only approved mentors appear in discovery. Existing relationships do not hide an approved mentor. No mentor self-withdrawal/deactivation filter in MVP.

### WF-02 — Mentorship request and introduction
Employee submits original request questions → browses approved mentors → selects candidate → invitation and response recorded → introductory conversation arranged outside the app if needed → mentor and protégé independently confirm willingness to proceed. Declined or withdrawn pairings are not formed relationships.

### WF-03 — Agreement-draft lifecycle and formation
**Step 1 — Draft preparation.** Mutually accepting, identified parties compose/review the current draft with agreement terms and individual goals. The draft has a distinct content identity.

**Step 2 — Current-draft acceptance.** Each participant independently accepts the same identified current draft. The exact content snapshot accepted by either participant cannot be overwritten.

**Step 3 — Formation approval.** After both currently applicable acceptances and the pairing prerequisites, Talent Management may approve the current draft. Rejected, stale or superseded decisions are not valid prerequisites.

**Step 4 — Formed relationship.** The system forms the initial `In Progress` relationship only when all current prerequisites are satisfied. Its agreement content is fixed. Initial `IsActive` must be Y or N; its choosing actor/default is unresolved as OPEN-002.

**Substantive pre-formation revision after either acceptance:** create a replacement draft, preserve earlier accepted snapshot/evidence, invalidate applicability of **all prior draft-specific acceptance states, statuses and decisions**, and return to **Step 1**. Both parties repeat acceptance of the replacement draft; Talent Management performs a new applicable formation approval. Pairing decisions remain effective only if the same parties and independently valid pairing remain; changing a named party also requires renewed pairing.

**Permitted administrative/non-substantive revision:** only a change explicitly authorised by the system, demonstrably without effect on content meaning, rights, obligations, parties, goals or terms, and recorded with actor/time/content/permission basis may avoid reset. No general exemption for formatting/metadata is assumed. If there is no explicit permission, the exception cannot be used; treat the proposed change as substantive or reject it.

**After formation:** no draft-replacement path can modify the formed agreement. Different terms, goals or mentor require a distinct relationship and full formation prerequisites.

### WF-04 — Goal progress and mentor completion decision
Protégé submits dated progress against immutable agreed goal wording → may request completion → mentor independently approves or rejects → request outcome and goal status are retained. Ordinary progress does not require approval. Pending/rejected requests do not mark completion; no automatic retry/escalation exists.

### WF-05 — Formed relationship administration
Talent Management may change `State` among In Progress/On hold/Complete in either direction, set/clear `Status` Needs reassigning and explicitly set `IsActive` Y/N independently; even unusual combinations are permitted. No admin edit bypasses formation or triggers mentor reassignment. Actor, timestamp and old/new values are audited.

### WF-07 — Register, reporting and historical data
An authorised person validates and manually enters historical relationships through a controlled existing mechanism; resolves real identities and maintains source-row provenance. Missing dates and unresolved source values remain review exceptions, not guessed facts. Talent Management can view/export the authorised register. This is not an automatic importer or a newly mandated admin screen.

## 5. Functional requirements and acceptance criteria
**Convention:** IDs match earlier BRS/YAML identifiers; retired IDs are not reused. New draft requirements are FR-067–FR-070. The candidate implementation is subject to formal business approval of the full BRS, not to a repeated approval question for every settled scope simplification.

### 5.1 Identity and access

**FR-001 — The application shall authenticate employees through the company Microsoft Entra ID implementation.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-001-01:** An employee with a permitted Entra identity can sign in without a separate application password.
- **AC-FR-001-02:** Unauthenticated requests to protected resources are rejected.
- **AC-FR-001-03:** Production identity acceptance requires Microsoft Entra ID. A labelled simulated identity in the prototype is not treated as a successful production Entra integration test.

**FR-002 — The application shall use the authenticated Entra identity as the stable reference for each employee, independent of name or seniority level.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05; SRC-04*
- **AC-FR-002-01:** Two employees with the same name can have distinct profiles.
- **AC-FR-002-02:** Changing a profile display name does not change the employee association on existing records.

**FR-003 — The application shall distinguish the functions of protégé, mentor, and Talent Management administrator.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-003-01:** Access and available actions differ by assigned role.
- **AC-FR-003-02:** An employee may perform both mentor and protégé functions.

**FR-004 — The application shall authorise each protected action on the server, including approval and administrative edits.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-004-01:** An unauthorised user cannot approve a mentor or edit another employee's relationship by invoking an API directly.
- **AC-FR-004-02:** Authorisation applies regardless of the interface used, including a prototype that explicitly simulates user identities.

**FR-005 — Employees shall be able to create and maintain their own application profile, without an MVP dependency on Profile Creator or the corporate Data Lake.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-005-01:** An authenticated employee can save their own profile details.
- **AC-FR-005-02:** The profile remains available when no external employee-profile integration is configured.

**FR-006 — Users shall not be able to change their authoritative Entra identity through profile editing.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-006-01:** Changing editable profile details does not change the linked Entra employee identity.

### 5.2 Mentor registration and discovery

**FR-007 — Any authenticated employee shall be permitted to apply to become a mentor.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-007-01:** The mentor registration action is available to an authenticated employee who has not previously registered as a mentor.

**FR-008 — A mentor registration shall require Talent Management approval before that mentor is discoverable.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-008-01:** A pending mentor does not appear in the protégé's mentor list.
- **AC-FR-008-02:** Approval causes the mentor to become eligible for discovery.

**FR-009 — The system shall make all registered and approved mentors available for discovery, regardless of the number of existing mentorship relationships.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-009-01:** An approved mentor is included even if already mentoring several people.
- **AC-FR-009-02:** No numerical mentor capacity limit prevents new requests.
- **AC-FR-009-03:** No unapproved mentor availability flag or relationship count silently removes an approved registered mentor from the discovery list (DEC-006).

**FR-010 — Mentors shall be able to maintain a concise biography and relevant professional attributes for protégé review.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01*
- **AC-FR-010-01:** A mentor can edit a brief biography and recorded experience attributes.
- **AC-FR-010-02:** A protégé can read those attributes for discoverable mentors.

**FR-011 — Mentor profiles shall support a role/title and separately captured industry experience and mentoring experience.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01*
- **AC-FR-011-01:** Profile display and discovery can distinguish job title, industry experience and mentoring experience.

**FR-012 — The mentor discovery interface shall present available approved mentors together with their profiles.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-012-01:** A protégé can browse multiple approved mentors and open their summaries.

**FR-013 — No inferred suitability score or automated AI ranking shall be a prerequisite for mentor discovery in MVP.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-013-01:** The MVP mentor list functions without an AI inference service.

### 5.3 Mentorship request and introduction

**FR-014 — An authenticated employee shall be able to submit a mentorship request containing the motivation for seeking mentorship.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-02*
- **AC-FR-014-01:** The request records the stated motivation and the submitting protégé identity.

**FR-015 — A mentorship request shall capture the prospective protégé's technical and career development goals.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-02*
- **AC-FR-015-01:** The protégé can submit technical and career aspirations as part of the request.

**FR-016 — A mentorship request shall capture what the protégé expects beyond technical skills.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-02*
- **AC-FR-016-01:** The request stores the nontechnical expectations separately from technical and career goals.

**FR-017 — The application shall retain the original mentorship request distinctly from any subsequent approved agreement.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-01; SRC-02*
- **AC-FR-017-01:** A request remains identifiable before or after a mentorship agreement is formed.

**FR-018 — A protégé shall be able to express interest in a discoverable mentor and arrange an introductory conversation.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01*
- **AC-FR-018-01:** The selected mentor and invitation/request can be associated with the protégé's request.
- **AC-FR-018-02:** A proposed introduction remains associated with the candidate and can be marked arranged outside the application; no calendar API is required.

**FR-019 — The system shall support a recorded positive or negative response from the prospective mentor to a proposed introduction or pairing.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-01*
- **AC-FR-019-01:** A mentor can accept or decline a pending invitation.
- **AC-FR-019-02:** A declined invitation is not interpreted as mutual acceptance.

**FR-020 — The system shall record an independent acceptance decision from each participant before a mentorship is formed.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-020-01:** Acceptance by only one person does not satisfy mutual acceptance.
- **AC-FR-020-02:** The system identifies the accepting mentor and protégé independently.

**FR-021 — Unsuccessful or withdrawn prospective pairings shall not be represented as active mentorship relationships.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-01; SRC-05*
- **AC-FR-021-01:** A failed pairing does not create an In Progress relationship.
- **AC-FR-021-02:** A withdrawn pairing retains its withdrawn disposition and does not acquire a formed relationship or reuse its prior draft approval decisions.

### 5.4 Agreement drafting, acceptance and formation

**FR-022 — Following mutual willingness to proceed, the identified mentor and protégé shall be able to prepare and review a digital agreement draft containing the substantive content of the existing mentorship agreement. Prior to formation, draft replacement follows FR-067–FR-070; it is not an amendment to a formed agreement.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-FR-022-01:** The digital agreement reproduces the original agreement's substantive commitments: voluntary partnership; identified mentor and protégé; term/duration and review expectations; meetings/contact; development activities and SMART goal guidance; barriers and contingencies; respective roles; confidentiality; and honest constructive feedback/open reception of feedback.
- **AC-FR-022-02:** The complete content presented to each signatory is available for review before acceptance, including fixed policy text as well as any entered values.

**FR-023 — The agreement shall support the duration categories 6 months, 12 months, and 12+ months.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-FR-023-01:** Each of the three duration categories can be saved.
- **AC-FR-023-02:** 12+ does not require a fixed end date.

**FR-024 — Agreement start and end dates shall each be optional.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-024-01:** An agreement can be stored with either or both dates empty.
- **AC-FR-024-02:** Where entered, each date remains available for display and reporting.

**FR-025 — The agreement shall capture its meeting frequency, approximate meeting duration, out-of-meeting contact expectations, missed-meeting arrangements, and agreed review date, where supplied.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03*
- **AC-FR-025-01:** The agreement represents the intended meeting interval in weeks, approximate minutes per meeting, limits on between-meeting telephone/email contact using the source's weeks/days wording, arrangements when either party cannot attend, and the date for review where supplied.
- **AC-FR-025-02:** The accepted terms communicate the original commitments to avoid cancelling except where unavoidable, agree the next meeting date at each meeting, and evaluate the relationship every three to six months. These are participant commitments, not automated enforcement or reminders.

**FR-026 — The agreement shall capture defined development goals, potential barriers, contingency plans, mentor/protégé responsibilities and the original commitments on confidentiality and constructive feedback.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03*
- **AC-FR-026-01:** Individual goals, guidance to form goals using SMART principles, barriers, contingency plans and the distinct agreed duties of mentor and protégé are included in the same accepted content.
- **AC-FR-026-02:** The accepted agreement includes confidentiality of meeting discussions, mentor honesty and constructive feedback, protégé openness to feedback, and the original expectation of concluding the relationship by amicable agreement once useful development is achieved. These commitments do not create a discussion-notes store or automatic completion workflow.

**FR-027 — Each identified participant shall independently accept the same current agreement draft content electronically; earlier acceptances of superseded drafts do not count towards formation.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-FR-027-01:** In production, each acceptance is attributed to the distinct Entra-identified participant. A prototype may demonstrate separate acceptances with explicitly simulated identities, but this does not satisfy production identity acceptance.
- **AC-FR-027-02:** Neither participant's acceptance is inferred from the other's action.
- **AC-FR-027-03:** Both applicable acceptances identify the same current agreement draft content; an earlier acceptance of superseded content cannot be combined with a later one.
- **AC-FR-027-04:** After replacement, both participants must accept the replacement draft; neither prior acceptance fulfils the requirement.

**FR-028 — The application shall retain the actual actor, timestamp, exact content identity and current applicability of every draft acceptance, including acceptances that are invalidated by a subsequent substantive revision.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-028-01:** An administrator can verify distinct current and superseded draft acceptances, each actual actor and timestamp, exact content reference and applicability.
- **AC-FR-028-02:** The system distinguishes retained historical acceptance evidence from current applicable acceptance for the active draft.

**FR-029 — From the first participant acceptance, that exact accepted draft content shall be preserved unchanged. A permitted substantive pre-formation revision creates a replacement draft and resets its workflow under FR-067; it never edits or erases the previously accepted content. The formed agreement remains immutable.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-029-01:** An attempt to modify an already accepted content snapshot is rejected or leaves that snapshot unchanged; a substantive pre-formation replacement is a new draft, not an edit to the accepted snapshot.
- **AC-FR-029-02:** An acceptance of different content cannot be combined with the first acceptance to form a relationship.
- **AC-FR-029-03:** A pre-formation replacement draft makes earlier acceptances inapplicable, returns to Step 1 and requires fresh acceptance from both participants. This behaviour is included in MVP.
- **AC-FR-029-04:** A substantive change after the first acceptance leaves the prior accepted snapshot untouched, creates a distinct replacement draft and resets the workflow to Step 1.

**FR-030 — A Talent Management administrator shall approve formation only after the identified mentor and protégé have accepted the same current draft and the required pairing decisions are valid; any superseded draft's acceptance or approval decision is inapplicable.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-030-01:** Neither two participant acceptances without administrator approval nor approval against superseded content can create an In Progress mentorship.
- **AC-FR-030-02:** The approval identifies the approving administrator.
- **AC-FR-030-03:** An administrator cannot form a relationship from superseded draft content or a superseded formation decision.

**FR-031 — An In Progress mentorship shall first be formed only when current mutual pairing acceptance, both participants' valid acceptances of identical current agreement content and Talent Management approval are all present. A draft revision shall not allow stale decisions to satisfy these prerequisites.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05 | Open decision: OPEN-002 formation-time IsActive*
- **AC-FR-031-01:** Omitting any current prerequisite, or relying on an invalidated draft acceptance or decision, prevents initial relationship formation.
- **AC-FR-031-02:** Only all three current prerequisites for the same candidate, same draft and same parties permit formation; historic legacy rows do not assert these events.

**FR-032 — After relationship formation, different agreed terms, goals or mentor require a separate mentorship request, agreement, acceptance and approval process. Before formation, revisions to the same parties' agreement draft follow FR-067–FR-070; a change in mentor/protégé identity also requires new matching and mutual acceptance for the new parties.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-032-01:** The formed agreement and its record remain immutable; old draft snapshots and their event evidence also remain unchanged.
- **AC-FR-032-02:** After formation, a change of mentor or agreed terms uses a separate request, agreement, acceptances and formation approval, with no transfer of goals.
- **AC-FR-032-03:** Before formation, a substantive change between the same identified participants restarts the draft workflow; after formation, it cannot change the formed agreement and requires a new relationship.

**FR-033 — The application shall retain auditable pairing decisions, draft versions/snapshots, participant acceptances, invalidation events and formation approvals independently from mutable relationship administration fields; superseded decisions remain historical evidence, not current approvals.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-033-01:** Later changes to State, Status or IsActive do not erase original formation evidence. Historical manually entered records remain identified as legacy and have no fabricated digital formation evidence.

### 5.5 Goal and progress management

**FR-034 — The application shall represent development goals individually so that their progress can be monitored separately.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-03*
- **AC-FR-034-01:** Each goal can be identified and shown with its own progress state.

**FR-035 — The protégé shall be able to submit progress information against an agreed development goal.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-035-01:** A protégé can submit a dated progress update against a goal in their relationship.

**FR-036 — The protégé shall be able to request formal completion of a development goal.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-036-01:** A submitted completion request is visibly pending until the mentor decides, without changing the goal to mentor-approved complete.

**FR-037 — The mentor shall be responsible for approving the completion of a development goal.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-037-01:** A protégé completion request remains unconfirmed until the mentor approves it.
- **AC-FR-037-02:** The mentor identity, actual decision (approved or rejected) and timestamp are retained. A rejection leaves the agreed goal incomplete; no rejection-reason field or resubmission workflow is assumed (DEC-008).

**FR-039 — Talent Management administrators shall not be approvers of progress submissions or goal completion.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-039-01:** Goal completion approval is possible only through the identified mentor action without any administrator approval step.

**FR-040 — The administrator may view development goals and their progress for relationship administration and reporting.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-040-01:** A Talent Management administrator can view the goal and structured progress information.

### 5.6 Relationship administration

**FR-043 — The application shall store State, Status and IsActive as distinct relationship attributes.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-04; SRC-05*
- **AC-FR-043-01:** Changing one attribute does not require a change to either of the other two.

**FR-044 — State shall allow exactly In Progress, On hold and Complete as business values.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-044-01:** Each named State can be saved by an authorised administrator for a formed operational relationship. Unresolved historical State is held as unknown during manual review; it is not a fourth State value.

**FR-045 — Status shall allow Needs reassigning or blank as business values. This is a register/administration field and does not imply an in-app mentor-reassignment workflow.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-045-01:** An administrator can set or clear Needs reassigning for any State without starting a reassignment workflow.

**FR-046 — IsActive shall allow Y and N as business values.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-046-01:** An administrator can select Y or N for any State and Status combination on a formed relationship. An unverified historical Active cell does not automatically supply IsActive (OPEN-002 governs the initial formed-relationship value).

**FR-047 — For an already formed mentorship relationship, Talent Management administrators shall be able to change State from any permitted value to any other permitted value. These edits shall not bypass the initial formation prerequisites.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-047-01:** Complete can be changed to In Progress or On hold.
- **AC-FR-047-02:** In Progress can be changed directly to Complete; no intermediate state is required. An unapproved candidate cannot be activated by an administrator State edit.

**FR-048 — Talent Management administrators shall be able to change Status independently of State.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-048-01:** Needs reassigning can be set or cleared while State remains unchanged.

**FR-049 — Talent Management administrators shall be able to override IsActive independently of both State and Status.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-049-01:** Complete with IsActive=Y can be saved.
- **AC-FR-049-02:** In Progress with IsActive=N can be saved.

**FR-050 — Any default or automatically suggested IsActive value shall not prevent or later silently overwrite an administrator's explicit choice.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-050-01:** An explicit administrator override remains effective following unrelated state or status changes.

**FR-051 — The application shall retain an audit history of changes to State, Status and IsActive.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-051-01:** Each recorded change identifies prior value, new value, acting administrator and timestamp.

**FR-054 — Mentorship relationship records shall support more than one relationship involving the same mentor or protégé over time.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-054-01:** Two historical relationships involving the same person remain separately identifiable.

### 5.7 Meetings, notifications and scheduling

**FR-055 — The agreement shall record planned meeting frequency and approximate meeting duration.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03*
- **AC-FR-055-01:** Agreed meeting frequency and duration are viewable by both participants.

**FR-056 — The application shall support representation of proposed introductory meetings and planned recurring mentorship sessions in the prototype.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-056-01:** A prototype user can view proposed or scheduled sessions for the example relationship.

**FR-057 — The application shall support notifications or demonstrable notification events for material workflow actions.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-01; SRC-05*
- **AC-FR-057-01:** For invitations, mentor responses, mutual-agreement actions, participant agreement acceptance, administrator approval and mentor goal-completion decisions, the prototype can identify the material action, intended recipient/role and required next action where applicable.
- **AC-FR-057-02:** The outcome is demonstrable in the prototype through an in-app notice, event display or explicitly simulated delivery. No delivery channel, queue, reminder schedule or production notification service is implied.
- **AC-FR-057-03:** Notification/event visibility is restricted to the intended authorised recipient; another unrelated employee cannot retrieve pending-action details.

**FR-058 — Production-grade live calendar synchronisation, availability search and rescheduling exception management shall not be dependencies of the first prototype.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05*
- **AC-FR-058-01:** The prototype can complete its demonstration without live calendar API access.

**FR-059 — The system shall not require storing private meeting discussion content to record a meeting or update a development goal.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-FR-059-01:** A meeting record can be created without private discussion notes.

### 5.8 Reporting, audit and legacy data

**FR-060 — Talent Management shall be able to view a mentorship relationship register containing mentor, protégé, levels, State, Status, IsActive, dates and duration where available.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-04; SRC-05*
- **AC-FR-060-01:** The authorised register/export displays the available spreadsheet-compatible fields without converting source unknowns to invented values; historical provenance and unresolved mappings are distinguishable from digitally formed data.
- **AC-FR-060-02:** An unauthorised employee cannot access the relationship register or its export through the user interface or API.

**FR-061 — The authoritative application records shall reside in a structured data store; the spreadsheet shall be a reporting/export view rather than an independently edited authoritative store.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-04; SRC-05*
- **AC-FR-061-01:** The reporting extract can be reconstructed from stored application records without treating the spreadsheet as a second authoritative source or requiring automatic synchronisation.

**FR-062 — The system shall preserve unknown or absent historic dates rather than fabricating dates during manual legacy data entry.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-04; SRC-05*
- **AC-FR-062-01:** A legacy record with an unknown start or end date is retained without a guessed date.
- **AC-FR-062-02:** Non-date source text such as "ongoing" remains traceable as source evidence, not a fabricated date; ambiguous textual dates are not interpreted without an explicit mapping decision.

**FR-063 — Initial legacy records may be entered manually through controlled database entry or an existing administrative mechanism, by a person authorised for that work; validation, identity resolution, provenance and accountability shall be preserved without an automated importer or new migration interface.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-05 | Open decision: OPEN-001 historical entry authority*
- **AC-FR-063-01:** The MVP does not depend on an automated legacy importer, new entry screen or migration subsystem.
- **AC-FR-063-02:** Where a record is admitted as authoritative, all applicable available historical values are retained without distortion, its participants are resolved to correct employee identities, references are valid, and the original source row/file and manual-entry actor/action time are traceable.
- **AC-FR-063-03:** Records with unresolved identities or unsupported State/Status/IsActive interpretation remain identifiable as review exceptions instead of being assigned guessed identities/values. The original spreadsheet remains available as provenance; no new staging application is prescribed.
- **AC-FR-063-04:** Manual history does not generate invented relationship-formation approvals, digital acceptances or historical action timestamps. Authority to perform entry is pending OPEN-001.

**FR-064 — The application shall maintain separately identifiable employee identities, requests, accepted agreements, formed relationships, goals, progress entries and approvals.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-01; SRC-02; SRC-03; SRC-04; SRC-05*
- **AC-FR-064-01:** A single mutable spreadsheet-style row is not the only retained record of digitally formed relationships; manually entered historical records carry a separate provenance indicator and do not imply missing digital lifecycle objects exist.

**FR-065 — Business-critical actions shall retain sufficient identity, timestamp and outcome information to be auditable.**
*Priority: Must | Origin: Derived | BRS evidence: SRC-05*
- **AC-FR-065-01:** Actual mutual acceptance, agreement acceptance, mentor goal-completion decision, administrator formation approval, administrative overrides and accountable manual historical entry are traceable where they occurred; legacy records are not backfilled with fictitious events.

**FR-066 — The application shall allow Talent Management to retrieve structured goals and progress without access to private meeting discussion content.**
*Priority: Must | Origin: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-FR-066-01:** An administrator can see goal completion status while private discussion content is not available through reports or APIs.

### 5.9 Draft-revision controls (new)

**FR-067 — If the substantive content or terms of an agreement draft change after either party accepts it and before the mentorship is formed, the system shall create a replacement draft and reset the agreement-draft workflow to Step 1 (draft preparation). All previous draft acceptance states, statuses and draft-specific decisions become inapplicable; both parties shall accept the replacement draft anew before administrator formation approval.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-FR-067-01:** After either party accepts draft A, editing a substantive term produces draft B with Step 1 status and no applicable participant acceptances or draft-specific approval decisions.
- **AC-FR-067-02:** Both participants independently accept draft B before formation may be approved.
- **AC-FR-067-03:** Prior draft A acceptance status and decisions remain in audit history but are not effective for draft B.

**FR-068 — A change after draft acceptance may avoid workflow reset only if it is demonstrably administrative or non-substantive, does not change meaning, obligations, rights, parties, goals or terms, and is explicitly permitted and recorded by the application. Such a change must not overwrite the exact content snapshot already accepted; its record must distinguish the allowed change from accepted content. All other changes must follow FR-067 or be rejected.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-FR-068-01:** A permitted, audited, non-substantive change that preserves the meaning and commitments and does not overwrite the accepted content snapshot does not reset the draft workflow.
- **AC-FR-068-02:** An unspecified/unpermitted edit is never silently classified as non-substantive; it is blocked or treated as substantive and triggers reset.
- **AC-FR-068-03:** Any change to obligations, rights, parties, goals or terms cannot use the non-substantive exception.

**FR-069 — The system shall preserve superseded draft content, previous acceptances and draft-specific decisions as historical audit evidence, identifying the actor, time, revision and invalidation outcome; superseded evidence shall not count toward formation.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-FR-069-01:** For every supersession, current and prior content can be distinguished; actual actor, time, reset outcome and prior decisions can be audited without making old approvals effective.

**FR-070 — The system shall validate draft currency and applicability of each prerequisite at the moment of formation, preventing an administrator approval based on superseded content or decisions; following a substantive draft replacement, both fresh participant acceptances and a new applicable formation approval are required.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-FR-070-01:** A simultaneous revision and approval cannot create a relationship from outdated acceptance evidence.
- **AC-FR-070-02:** When draft B replaces draft A, only valid decisions attributable to draft B can support the eventual administrative formation approval.

**Retired functional IDs:** FR-038, FR-041, FR-042, FR-052 and FR-053. Their removed behaviours may not be inferred as implied requirements.

## 6. Explicit business rules
All rules below reflect the current candidate scope. Historic approval questions for settled rules are not reintroduced.

**BR-001 — Any Entra-authenticated employee may apply to mentor; only Talent Management-approved mentors are discoverable.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-001-01:** Any Entra-authenticated employee can submit a mentor application, and only mentors approved by Talent Management appear in discovery.

**BR-002 — Approved mentors have no numerical limit on simultaneous relationships.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-002-01:** An approved mentor with any number of existing relationships can still be selected for a new request.

**BR-003 — An In Progress mentorship is first formed only after mutual acceptance, acceptance of the agreement by both participants and administrator approval.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-E2E-01:** E2E-01 End-to-end formation: all approved mentors appear; protégé requests contact; both independently agree to proceed, accept identical agreement content and Talent Management approves; State is initially In Progress. The formation-time IsActive treatment remains a pending policy choice (OPEN-002), not a silent deduction from State.
- **AC-E2E-02:** E2E-02 Missing prerequisite: with one of mutual acceptance, both agreement acceptances or administrator approval missing, no initial In Progress formation occurs.

**BR-004 — The accepted content of each draft snapshot is preserved unchanged. A substantive pre-formation change creates a replacement draft, invalidates all prior draft-specific acceptance states/statuses/decisions and restarts Step 1; both parties accept the replacement. Once a relationship is formed, its agreement content, parties, terms and goals are fixed and different terms or mentor require a new relationship.**
*Status: core integrity Derived; fixed-goal/no-amendment scope Confirmed; conditional draft Scope fixed (20 Oct 2026) | BRS evidence: SRC-03; SRC-05; v0.2*
- **AC-E2E-05:** E2E-05: One participant accepts draft A; a substantive change produces draft B, resets to Step 1 and invalidates the applicability of all draft A decisions without deleting history. Both parties accept B, then Talent Management approves. After formation the accepted agreement is immutable.

**BR-005 — The protégé may submit ordinary progress without approval and request formal goal completion. The identified mentor alone approves or rejects completion; a pending or rejected request does not mark the goal complete. Agreed goal wording is immutable after formation. No rejection-reason requirement, resubmission loop, escalation or goal-amendment workflow is included in MVP.**
*Status: approval authority Confirmed; pending/rejection semantics Derived; no-amendment Scope fixed (20 Oct 2026); repeat request Scope fixed (20 Oct 2026) | BRS evidence: SRC-01; SRC-03; SRC-05; v0.2*
- **AC-E2E-04:** E2E-04 Goal workflow: progress does not require approval; completion request remains visibly pending and incomplete until the mentor alone approves. A mentor rejection remains auditable, leaves the goal incomplete and introduces no extra administrator stage, reason mandate or resubmission loop.

**BR-006 — For formed operational relationships, State is In Progress, On hold or Complete; Talent Management may move between these in either direction. Drafts and unformed candidates have no operational relationship State. Legacy unresolved source values remain separate review exceptions.**
*Status: Confirmed value set; agreed administration rule | BRS evidence: SRC-04; SRC-05; v0.2*
- **AC-BR-006-01:** A formed relationship accepts only In Progress, On hold or Complete as State, each reachable from each other by an administrator; an unformed candidate cannot be given a State; no "Unknown" State value exists.

**BR-007 — Status values are Needs reassigning and blank. Talent Management edits Status independently on formed relationships; the label does not trigger reassignment.**
*Status: Confirmed values; independent administration scope fixed and no-reassignment DEC-002 | BRS evidence: SRC-04; SRC-05*
- **AC-BR-007-01:** Status accepts only Needs reassigning or blank; setting Needs reassigning changes no other attribute and starts no reassignment workflow.

**BR-008 — IsActive is Y or N for formed operational relationships and is independently editable by Talent Management, including unusual combinations. The initial formation-time value or choosing actor remains to be decided under OPEN-002; historical 'Active' text cannot be mapped automatically.**
*Status: Confirmed value set; override interpretation scope fixed; initial value pending OPEN-002 | BRS evidence: SRC-04; SRC-05 | Open decision: OPEN-002 formation-time IsActive*
- **AC-BR-008-01:** The formation-time IsActive value is an outstanding business decision (OPEN-002); formed relationships accept Y/N independently of State/Status, and legacy Active text is not blindly copied.

**BR-009 — The application does not reject unusual State/Status/IsActive combinations for formed relationships or silently overwrite explicit administrator choices; this rule does not authorise forming relationships without all prerequisites.**
*Status: Confirmed behaviour; Talent baseline scope fixed | BRS evidence: SRC-05*
- **AC-E2E-03:** E2E-03 Independent administration: for a formed relationship, admin saves Complete + Needs reassigning + IsActive=Y, then On hold + blank + IsActive=N; both are retained and audited. Neither change triggers reassignment.

**BR-010 — Duration categories are 6 months, 12 months and 12+ months. A 12+ duration may be open ended.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-010-01:** An agreement can be saved with duration 6 months, 12 months or 12+ months, and a 12+ agreement can be saved without an end date.

**BR-011 — StartDate and EndDate are optional for all duration categories.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-011-01:** An agreement of any duration category can be saved with StartDate and/or EndDate empty.

**BR-012 — Separate digital agreement acceptances replace handwritten signatures.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-012-01:** Formation relies on two separately recorded digital acceptances and no handwritten signature or scanned document is required.

**BR-013 — Administrators may view structured goals and progress but not private meeting discussions.**
*Status: Confirmed | BRS evidence: SRC-03; SRC-05*
- **AC-E2E-08:** E2E-08 Confidentiality: administrator views goals/progress but cannot access private discussions via UI, API, export or logs.

**BR-015 — Participants maintain their profiles in the app; MVP does not depend on the Data Lake or another employee-profile integration.**
*Status: Confirmed | BRS evidence: SRC-05*
- **AC-BR-015-01:** Profile creation and maintenance complete successfully with no Data Lake or employee-profile integration configured.

**BR-016 — Historical records are entered manually using controlled entry, identity resolution and valid references; source values/provenance, missing dates and actor accountability are retained. Legacy records are not represented as digitally accepted/approved; unresolved source rows remain review exceptions. No automated importer or new entry UI is mandated.**
*Status: manual method Confirmed; fidelity/accountability Derived; entry authority pending OPEN-001 | BRS evidence: SRC-04; SRC-05 | Open decision: OPEN-001 historical entry authority*
- **AC-E2E-07:** E2E-07 Legacy record: a validated historical row can be entered manually and reported with its real source provenance, unresolved/missing dates and legacy classification; unresolved identities remain review exceptions; no fabricated digital agreements, acceptances, approvals or timestamps. "ongoing" is not turned into a date, and "Active" is not blindly used as IsActive.

**BR-017 — Agreements capture the source meeting cadence/duration, cancellation, missed-meeting, next meeting, between-meeting contact, and three-to-six-month review commitments. These are participant commitments only; no automatic policing, reminders, overdue/closure rules or enforcement workflow are in MVP.**
*Status: source content Confirmed; no automatic enforcement Scope fixed (20 Oct 2026) | BRS evidence: SRC-03; v0.2*
- **AC-BR-017-01:** Meeting/review commitments appear in the accepted agreement without automatic reminders, policing or completion rules.

**BR-018 — The original agreement binds participants to meeting confidentiality; MVP does not store private discussion notes. Structured goals and progress remain visible to permitted roles.**
*Status: source obligation Confirmed; data boundary Confirmed/Derived | BRS evidence: SRC-03; SRC-05*
- **AC-BR-018-01:** The accepted agreement carries the meeting confidentiality commitment; no field, API or report exists for private discussion notes; structured goals and progress are visible to the permitted roles.

**BR-019 — Only currently applicable acceptances of the same current draft by the identified mentor and protégé count. Substantive draft replacement invalidates all earlier draft-specific acceptance states, statuses and decisions and starts Step 1 again; historical records are retained but have no approval effect.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-BR-019-01:** A draft acceptance from superseded content never satisfies the new draft approval prerequisites.

**BR-020 — An explicitly permitted and recorded administrative or non-substantive change that does not affect draft meaning, rights, obligations, parties, goals or terms does not restart the draft workflow. It must not overwrite accepted snapshot content. No unrestricted edit permission is inferred; absent an approved permitted-change rule the change cannot use this exception.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-BR-020-01:** A record of an allowed non-substantive change shows permission basis, actual actor, time and changed field/content; the accepted meaning is unchanged.

**BR-021 — Agreement draft lifecycle values/decisions are separate from candidate pairing decisions and from formed relationship State/Status/IsActive. Changing an identified party also invalidates that party's earlier pairing and agreement acceptances and requires renewed pairing decisions before formation.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-BR-021-01:** Changing a named party before formation invalidates the original pairing/acceptances; operational State remains unavailable until current formation prerequisites are satisfied.

**Retired business-rule ID:** BR-014. No in-place mentor-reassignment rule is inferred.

## 7. Data requirements and conceptual entities
These are business information needs, not prescribed database tables. In particular, `AgreementDraftRevision` is a conceptual audit/integrity requirement, not an instruction to create a separate revision table.

**DR-001 — Employee: the application shall maintain the Entra object identifier for digitally authenticated employees; display name, optional level/title, owner; and historical identity-resolution evidence where relevant. Entra is the authority for authenticating current employees; historical names alone are not reliable identifiers and cannot generate fictitious identities.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-04; SRC-05 | Open decision: OPEN-001 historical entry authority*
- **AC-DR-001-01:** A digitally authenticated employee record carries its Entra object identifier, display name and optional level/title; a historical name without a resolved Entra identity cannot create an employee record.

**DR-002 — MentorProfile: the application shall maintain employee, biography, role/title, industry experience, mentoring experience and approval. Only approved mentors are discoverable; no numeric capacity cap.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-01; SRC-05*
- **AC-DR-002-01:** A mentor profile carries employee, biography, role/title, industry experience, mentoring experience and approval, and only an approved profile is returned by discovery.

**DR-003 — MentorshipRequest: the application shall maintain protégé, motivation, technical/career goals, nontechnical expectations, date and disposition. The request is different from the eventual agreement.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-01; SRC-02*
- **AC-DR-003-01:** A mentorship request carries protégé, motivation, technical/career goals, nontechnical expectations, date and disposition, and is retained as a record distinct from any agreement.

**DR-004 — MentorSelection: the application shall maintain request, candidate mentor, invitation and response, and dates. A declined mentor does not create a formed relationship.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-01*
- **AC-DR-004-01:** A mentor selection carries the request, candidate mentor, invitation, response and dates; a declined selection has no formed relationship.

**DR-005 — MentorshipRelationship: the application shall maintain participants, State, Status, IsActive, duration, optional dates, history and digital-versus-legacy provenance. Digitally formed only after all prerequisites; accepted history distinct from manually entered source data. Independent operational attributes; unresolved historical values do not become invented facts.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-04; SRC-05 | Open decision: OPEN-002 formation-time IsActive*
- **AC-DR-005-01:** A mentorship relationship carries participants, State, Status, IsActive, duration, optional dates, history and a digital-versus-legacy provenance indicator; a digitally formed relationship exists only after all prerequisites and a legacy relationship carries no invented operational values.

**DR-006 — MentorshipAgreement: preserve each current and superseded draft's complete substantive content, terms, goals, participants and stable content identity, and the formed immutable agreement snapshot. Pre-formation replacements never mutate or erase a previously accepted content snapshot. Conceptual entity, not a table prescription.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-03; SRC-05*
- **AC-DR-006-01:** Current draft, superseded drafts and formed accepted content retain distinct stable identities; previously accepted snapshots are never modified by replacement.

**DR-007 — AgreementAcceptance: retain exact draft/content identity, actual participant, time, current applicability and invalidation/supersession association. Both required current acceptances reference identical current content; historical superseded or legacy acceptance evidence cannot be fabricated or treated as current. Conceptual entity, not a table prescription.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-03; SRC-05*
- **AC-DR-007-01:** Each acceptance references a distinct actual participant and exact draft; superseded acceptances remain auditable but inapplicable.

**DR-008 — FormationApproval: associate the approving Talent Management administrator, actual action time, identified candidate and current draft content with the formation decision. Superseded draft-specific decisions cannot count; historical legacy relationships must not gain fictitious digital approvals. Conceptual entity, not a table prescription.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-01; SRC-05*
- **AC-DR-008-01:** The approval records actor, candidate, current accepted agreement content and real time; superseded decisions do not satisfy formation; none are fabricated for legacy relationships.

**DR-009 — DevelopmentGoal: the application shall maintain relationship/agreement goal reference, agreed description and completion status. Agreed goal text is fixed; status is updated separately from agreement content.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-03; SRC-05*
- **AC-DR-009-01:** A development goal carries its relationship/agreement reference, agreed description and completion status; the agreed description cannot be changed and the status changes without altering the agreement content.

**DR-010 — GoalProgress: the application shall maintain goal, protégé, dated progress update and optional completion request. Submitting progress does not mean approved completion.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-01; SRC-05*
- **AC-DR-010-01:** A goal progress entry carries goal, protégé, dated progress update and an optional completion request, and its existence does not set the goal to completed.

**DR-011 — GoalApproval: the application shall maintain goal completion request, mentor, approved/rejected decision and actual date/time. Pending requests are not approvals; rejection leaves goal incomplete; no rejection-reason or retry subsystem mandated.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-05*
- **AC-DR-011-01:** A goal approval carries the completion request, mentor, approved/rejected decision and actual date/time; a pending request is not an approval and a rejection leaves the goal incomplete.

**DR-013 — MeetingSchedule: the application shall maintain relationship, proposed/scheduled session time if available, and agreed frequency/duration. No private notes or live calendar dependency.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-03; SRC-05*
- **AC-DR-013-01:** A meeting schedule carries the relationship, proposed/scheduled session time where available and the agreed frequency/duration, with no private notes field and no live calendar dependency.

**DR-014 — AdministrationAudit: preserve actors, actions, affected records, previous/current values, real timestamps, draft revision/supersession/invalidation events, applicable decision context, and manual-entry source references. Actual application actions must be distinguishable from legacy-source claims. Conceptual entity, not a table prescription.**
*Conceptual entity, not a prescribed table | BRS evidence: SRC-04; SRC-05 | Open decision: OPEN-001 historical entry authority*
- **AC-DR-014-01:** An administration audit entry carries actor, action, affected record, prior/new values where applicable, true action date/time and manual-entry source reference, and distinguishes actual application events from source-claimed history.

**DR-015 — AgreementDraftRevision: conceptually identify draft revision, parent/superseded draft, whether each change is substantive or an explicitly authorised non-substantive change, actor, time, changed content identity and resulting workflow reset or permitted exception. Previously accepted copies remain unchanged. Conceptual entity, not a required extra database table.**
*Priority: Must | Origin: decision confirmed 20 Oct 2026 | Evidence: SRC-06*
- **AC-DR-015-01:** A substantial revision and a permitted non-substantive edit are traceably distinguishable, and the original accepted snapshot is preserved.

**Retired conceptual entity ID:** DR-012 (in-place mentor assignment history).

### 7.1 Legacy tracker field mapping
| Tracker value | Candidate interpretation | Constraint |
|---|---|---|
| Mentor / protégé names, levels | Identity-linked display/context | Names are not unique; unresolved identities are review exceptions; do not invent employees. |
| Active: Successfully complete | State = Complete | Derived, row-level validation needed; Status/IsActive unresolved. |
| Active: put on hold | State = On hold | Derived, row-level validation needed; Status/IsActive unresolved. |
| Active: No - needs reassigning | Status = Needs reassigning | IsActive=N is not authorised automatically; State unresolved. |
| Active: Yes | Potential State = In Progress and/or IsActive=Y | Only proposed and requires per-row validation. |
| Active: blank | Unknown | No automatic default. |
| End/Duration: ongoing | Keep original text | Unknown EndDate; do not manufacture a date. |
| Duration: 6 months | 6-month category | Only if the record is otherwise validated. |
| Row 8 Start Date: 10/1/2026 | Ambiguous locale/date | No automatic parsing until source meaning confirmed. |
| Rows 9 and 18: no mentor | Unresolved source relationship | Do not fabricate mentor, agreement or acceptance evidence. |

### 7.2 Authoritative and privacy boundaries
- Entra authenticates active employees and provides stable authoritative current identity; names alone do not create fictitious historic identities.
- Application database is the authoritative source for mentorship operations, approvals and current draft status.
- Every accepted content snapshot is preserved unchanged. Superseding a draft changes applicability of earlier decisions; it does not erase the record.
- The formed agreement is immutable. Goals can accrue separate progress/completion events without changing agreed goal wording.
- The tracker remains source/provenance and a reporting shape, never a concurrently edited second operational source.
- Private meeting discussions are never captured, returned or logged; structured goals/progress are accessible only to defined roles.

## 8. Non-functional requirements and acceptance criteria
**NFR-001 — Security: in production, authenticate through Entra and authorise protected operations at the server/API. An explicitly simulated prototype identity does not satisfy production authentication acceptance; server authorisation still applies.**
*Treatment: mandatory behaviour; Entra configuration external | BRS evidence: SRC-05*
- **AC-NFR-001-01:** In production every protected operation is authenticated through Entra and authorised at the server/API; a prototype run with labelled simulated identities still enforces server authorisation and is not recorded as production authentication acceptance.

**NFR-002 — Privacy: do not capture private meeting discussion notes in MVP, including in administrator screens, exports, APIs or logs.**
*Treatment: mandatory privacy boundary; no private-notes feature in MVP | BRS evidence: SRC-03; SRC-05*
- **AC-NFR-002-01:** No administrator screen, export, API or log captures or returns private meeting discussion notes.

**NFR-003 — Data integrity: maintain valid references across employee identities, agreements, requests, goals, relationships and recorded approvals; unresolved legacy identities cannot be silently replaced with guesses.**
*Treatment: required; data structures/transactions deferred to architecture | BRS evidence: SRC-04; SRC-05*
- **AC-NFR-003-01:** Every stored reference between employee identities, agreements, requests, goals, relationships and recorded approvals resolves to an existing record, and an unresolved legacy identity is held as a review exception rather than replaced with a guessed employee.

**NFR-004 — Auditability: retain true actor, action timestamp, relevant values and outcome for actual acceptances, approvals, goal decisions, administration and manual legacy entry; never invent historical event timestamps.**
*Treatment: required; retention period deferred | BRS evidence: SRC-04; SRC-05*
- **AC-NFR-004-01:** For each actual acceptance, approval, goal decision, administration change and manual legacy entry, the true actor, action timestamp, relevant values and outcome are retained, and no legacy record carries an invented event timestamp.

**NFR-005 — Agreement integrity: preserve the exact accepted content snapshot unchanged; ensure substantive pre-formation replacement invalidates old draft acceptance applicability without deleting evidence; ensure formed agreement content cannot be substantively amended. Technical version/hash/signature mechanisms are architecture decisions.**
*Treatment: required; hash/signing/version storage mechanics not specified | BRS evidence: SRC-03; SRC-05*
- **AC-NFR-005-01:** Following acceptance and a substantive draft revision, old content is unchanged, old decisions are inapplicable, new acceptance is necessary, and no formed agreement is altered.

**NFR-006 — User experience: deliver understandable, role-appropriate self-service screens for protégés, mentors and administrators in the separately delivered React frontend, backed by server-enforced permissions.**
*Treatment: product requirement unchanged; exact accessibility/browser targets deferred for production | BRS evidence: SRC-01; implementation allocation BRS §2.4*
- **AC-NFR-006-01:** Protégés, mentors and administrators can complete product workflows in understandable, role-appropriate screens of the separately delivered React frontend, with the backend providing secure, consistent business outcomes for the interface.

**NFR-007 — Availability/recovery: production operation requires an appropriate recoverability decision for the authoritative store; no quantitative target is specified by this BRS.**
*Treatment: DEFERRED - hosting, backup/recovery metrics and procedures before production acceptance; not a prototype-blocking target | BRS evidence: prior v0.2 design assumption*
- **AC-NFR-007-01:** Before production acceptance a recoverability decision (hosting, backup/recovery metrics and procedures) for the authoritative store is recorded; the prototype is not blocked by its absence.

**NFR-008 — Operational support: define production ownership and controlled operator/data access before production; no new admin module or configuration workflow is mandated.**
*Treatment: DEFERRED operating arrangement; entry role decision is OPEN-001 | BRS evidence: prior v0.2 design assumption*
- **AC-NFR-008-01:** Before production, production ownership and controlled operator/data access are defined without adding an admin module or configuration workflow to the product.

**NFR-009 — Data protection: limit personally attributable development information to permitted roles and protect it in storage/transit. Do not expose private meeting discussions through records, APIs, exports or logs.**
*Treatment: required principle; security detail, POPIA compliance controls and retention period defined before production | BRS evidence: SRC-02; SRC-03; SRC-05*
- **AC-NFR-009-01:** Personally attributable development information is readable only by permitted roles, is protected in storage and transit, and private meeting discussions are not exposed through records, APIs, exports or logs.

**NFR-010 — Prototype portability: prototype core flows shall function without live calendar, Data Lake, AI inference, live notification transport or completed Entra configuration where identity simulation is expressly identified.**
*Treatment: Confirmed/Derived prototype boundary; production identity requirement unchanged | BRS evidence: SRC-01; SRC-05*
- **AC-NFR-010-01:** The prototype core flows run with no live calendar, Data Lake, AI inference or live notification transport, and with expressly identified identity simulation where Entra configuration is not complete.

**Production readiness (not prototype blockers):** Hosting and recoverability metrics, operator/data-access model, POPIA controls and retention, accessibility/browser support, security evidence and production Entra integration must be resolved before production acceptance. No invented numerical targets are introduced here.

## 9. End-to-end acceptance scenarios
**AC-E2E-01:** E2E-01 End-to-end formation: all approved mentors appear; protégé requests contact; both independently agree to proceed, accept identical agreement content and Talent Management approves; State is initially In Progress. The formation-time IsActive treatment remains a pending policy choice (OPEN-002), not a silent deduction from State.

**AC-E2E-02:** E2E-02 Missing prerequisite: with one of mutual acceptance, both agreement acceptances or administrator approval missing, no initial In Progress formation occurs.

**AC-E2E-05:** E2E-05: One participant accepts draft A; a substantive change produces draft B, resets to Step 1 and invalidates the applicability of all draft A decisions without deleting history. Both parties accept B, then Talent Management approves. After formation the accepted agreement is immutable.

**AC-E2E-04:** E2E-04 Goal workflow: progress does not require approval; completion request remains visibly pending and incomplete until the mentor alone approves. A mentor rejection remains auditable, leaves the goal incomplete and introduces no extra administrator stage, reason mandate or resubmission loop.

**AC-E2E-03:** E2E-03 Independent administration: for a formed relationship, admin saves Complete + Needs reassigning + IsActive=Y, then On hold + blank + IsActive=N; both are retained and audited. Neither change triggers reassignment.

**AC-E2E-08:** E2E-08 Confidentiality: administrator views goals/progress but cannot access private discussions via UI, API, export or logs.

**AC-E2E-07:** E2E-07 Legacy record: a validated historical row can be entered manually and reported with its real source provenance, unresolved/missing dates and legacy classification; unresolved identities remain review exceptions; no fabricated digital agreements, acceptances, approvals or timestamps. "ongoing" is not turned into a date, and "Active" is not blindly used as IsActive.

**E2E-09 — Permitted non-substantive edit:** After participant acceptance, an explicitly authorised administrative/non-substantive change is audited and leaves the acceptance workflow valid when meaning/rights/obligations/terms are unchanged. An unpermitted change cannot use the exception.

**E2E-10 — Stale formation race:** Participant A accepts draft A; a substantive revision yields draft B; old acceptance and any draft-specific approval cease to be applicable. A stale attempt to form using draft A must fail. Both participants accept B and Talent Management approves B before formation.

**E2E-11 — Party change:** Before formation, a party substitution invalidates the old candidate’s pairing and draft decisions; fresh matching, acceptance and formation steps for the correct named parties are needed. After formation, a new relationship is necessary.

**Retired scenario IDs:** E2E-06 and workflow WF-06 are intentionally not reassigned (no in-place reassignment).

## 10. Interpretations, exclusions and external dependencies
### 10.1 Settled interpretations
| Ref | Agreed interpretation | Consequence |
|---|---|---|
| INT-001 | After formation, goals/terms are fixed; progress and completion are separate facts. Before formation, draft reset permits fresh content without editing earlier accepted snapshots. | No goal amendment, goal-change approval or formed-agreement versioning workflow. |
| INT-002 | A formed relationship retains its named mentor. | A different mentor requires a new relationship; no carry-forward/transfer. |
| INT-003 | An unformed candidate has no operational State. | Administrative State edits cannot bypass formation. |
| INT-004 | Ordinary progress is not approval-gated. | The mentor alone approves/rejects completion; no extra administrator stage. |
| INT-005 | Authenticated initial request submission does not require a separate signature. | Separate digital acceptance applies to the agreement only. |
| INT-006 | Introductions can be arranged outside the application. | No live calendar integration. |
| INT-007 | Complete is reversible for formed relationships. | No irreversible termination workflow. |
| INT-008 | Private meeting discussions are excluded. | Goals/progress are structured without private notes. |
| INT-009 | Meeting and review commitments are recorded but not policed. | No reminder, timer or automatic closure. |
| INT-010 | Formed relationships require explicit Y/N IsActive. | Formation-time actor/value remains OPEN-002; admin overrides remain authoritative. |
| INT-011 | Substantive accepted-draft changes before formation supersede the draft and reset Step 1. | Old evidence retained; old decisions invalid; new acceptances and approval needed. |
| INT-012 | Explicitly permitted, recorded non-substantive changes preserve approval workflow state. | No blanket exemption without permission rule or when meaning/terms change. |

### 10.2 External dependencies
| Ref | Dependency | Owner | Gate |
|---|---|---|---|
| EXT-001 | Production Microsoft Entra configuration/test identities | IT identity team | Production identity acceptance; prototype may simulate expressly. |
| EXT-002 | Hosting, backup, security operation, authorised database access | IT/platform owner | Production. |
| EXT-003 | Calendar API access | IT collaboration team | Outside MVP. |
| EXT-004 | Formal BRS approval, historical entry authority and initial IsActive decision | Talent Management + IT as applicable | Baseline and relevant functional acceptance. |
| EXT-005 | Whitelist/permission policy for administrative or non-substantive draft edits | Talent Management / designated owner | Before such no-reset edits are enabled; otherwise block them. |

## 11. Decision reconciliation and outstanding matters
### 11.1 Settled product-scope decisions — no repeat approval question
| Legacy reference | Disposition in v0.3.0 |
|---|---|
| APR-001 → DEC-001 | Closed: immutable formed agreements and fixed agreed goals; no post-formation amendment or goal amendment approval. |
| APR-002 → DEC-002 | Closed for candidate scope: no in-place mentor reassignment. |
| APR-003 → DEC-003 | Closed for candidate scope: independent State/Status/IsActive on formed relationships. |
| APR-004 → DEC-004 | Closed for candidate scope: agreement records meeting/review commitments without automatic enforcement. |
| APR-005 → DEC-005 | Resolved by SRC-06: accepted-draft substantive changes before formation reset workflow to Step 1; permitted/recorded non-substantive changes exempt. |
| APR-006 → DEC-006 | Resolved for candidate scope: all Talent Management-approved mentors remain discoverable; availability suppression excluded. |
| APR-008 → DEC-008 | MVP scope excludes rejected-completion retry/escalation/reason workflows; no new logic inferred from source silence. |

These entries document settled candidate scope. Talent Management must still approve the **complete BRS as a document** before the formal business baseline. This does not recreate a per-decision approval gate or reopen excluded workflows.

### 11.2 Genuine open decisions / dependencies
| Open ID | Question | Gate | Proposed treatment until resolved |
|---|---|---|---|
| OPEN-001 (was APR-007) | Who is authorised to validate and manually enter historical relationships? | Before authoritative legacy entry is accepted/operated. | Do not grant a new role/UI by implication; use a specifically authorised existing role. |
| OPEN-002 (was APR-009) | Who supplies the initial formed-relationship IsActive=Y/N value, and is there a default? | Business baseline / initial relationship formation test. | Require explicit selection in the prototype by approving admin only as labelled working assumption, not approved rule. |
| OPEN-003 | Which precise administrative/non-substantive draft changes may bypass reset, and which actor may perform them? | Before no-reset exception is enabled. | Until explicitly authorised, do not classify any post-acceptance change as exempt. |
| OPEN-004 | Can historic people absent from the Entra directory be represented as unresolved records inside the authoritative store? | Before claiming full historical-register completeness. | Retain source rows as review exceptions; never invent Entra identities. |

### 11.3 Other documented production/quality decisions
Before production acceptance, confirm privacy/retention policies, operational access, recovery objectives, accessibility/browser targets and any necessary evidence of Entra configuration and deployment controls. These are not prototype scope expansion requests.

### 11.4 Remaining source and traceability notes
- The prior BRS v0.2 was accessible as Project indexed content but not as a raw file. This revision takes the YAML v0.2.1 as the complete requirement/acceptance catalogue and reconciles it with the earlier BRS structure, supplied source documents and SRC-06. It is a reconstructed, substantially revised BRS, not a file-preserving edit of the inaccessible source bytes.
- The document status is **candidate**; the current instruction settles identified functional questions but does not claim Talent Management has signed the baseline.
- The tracker row-level historical conversions need validation. The legacy `Active` text is not identical to `IsActive`.
- The original source documents support the form questions and agreement content, but do not themselves prescribe the digital replacement/reset behaviour; that rule comes from SRC-06.

### 11.5 ID and coverage controls
- Retired IDs remain retired: FR-038, FR-041, FR-042, FR-052, FR-053, BR-014, DR-012, WF-06 and E2E-06.
- Added: FR-067–FR-070; BR-019–BR-021; DR-015; E2E-09–E2E-11. Existing requirement IDs retain their meaning except explicit reconciliations documented in §12.
- Acceptance criteria in §5–§9 are product-level tests; backend authorisation and integrity cannot be delegated solely to the frontend.

## 12. Change log and next gate
| Item | Change from previous BRS/YAML |
|---|---|
| 01 | Removed historical APR-001 and all repeated pending-approval caveats for settled scope decisions; preserved formal overall business approval. |
| 02 | Separated accepted immutable snapshots and immutable formed agreements from editable/supersedable pre-formation drafts. |
| 03 | Implemented 20 Oct draft reset: substantive change after either acceptance → replacement draft → all old draft decisions inapplicable → Step 1 → two new acceptances → new applicable TM approval. |
| 04 | Added explicitly authorised, audited, strictly non-substantive no-reset exception and default-deny policy until permissions are specified. |
| 05 | Corrected FR-032 so a pre-formation change does not automatically demand a completely new relationship, while post-formation changes do. |
| 06 | Removed contingent APR-005 acceptance language and reinforced stale-approval, withdrawal, actor-permission and audit criteria. |
| 07 | Separated two genuinely open business items (legacy entry authority, initial IsActive) and two dependent policy/coverage questions from already-settled scope decisions. |
| 08 | Recorded source provenance accurately: SRC-05 is defined in BRS v0.2; new instruction SRC-06; no unverified sign-offs invented. |

**Next step:** Business owner reviews this candidate v0.3.0 and closes OPEN-002 for a coherent baseline; OPEN-001 must be resolved before legacy entries become authoritative, OPEN-003 before the no-reset exception is enabled, and OPEN-004 before claiming full historic coverage. Then re-ingest this version into the Engineering Harness and validate the generated specification and architecture candidate. Existing YAML v0.2.1 is superseded for requirements interpretation by this candidate document but has **not** itself been amended.

