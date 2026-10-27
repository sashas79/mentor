<!-- DRAFT: requires operator review -->

# Mentorship App — Business Requirements Specification (v0.3, draft)

> This draft was written from a one-line description by the operator. Before it is used as the authoritative source for specification ingestion, the operator must review it and add detail.

## 1. Purpose

The Mentorship App is an internal utility application for capturing and managing mentorship relationships.

Operator description (verbatim):

> internal mentorship relationship capturing and management utility app

## 2. Scope

- **Audience:** internal to the organisation.
- **Subject:** mentorship relationships.
- **Capabilities stated:** capturing mentorship relationships, and managing mentorship relationships.

## 3. Business requirements

- **BR-1 — Capture mentorship relationships.** The application shall allow a mentorship relationship to be captured (recorded).
- **BR-2 — Manage mentorship relationships.** The application shall allow captured mentorship relationships to be managed.
- **BR-3 — Internal use.** The application shall be an internal application of the organisation.

## 4. Technology context

From the Project Engineering Pack: .NET (language), PostgreSQL (database), React (frontend).

## 5. Open points for operator review

These points are not stated in the source description and remain unresolved:

1. Which attributes make up a mentorship relationship (for example the participants, dates, goals or status)?
2. Which operations "manage" covers (for example update, end, archive, delete, search or report)?
3. Which user roles exist, and what is each one allowed to do?
4. How users are authenticated, and which identity provider is used?
5. Whether there are reporting, notification or integration needs.
6. Data retention and privacy requirements.
7. Non-functional requirements such as availability, performance and accessibility.
