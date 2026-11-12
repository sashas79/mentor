# Platform decisions for TASK-16-PLATFORM-OPERATIONS

Human-authored input to the work item. Authoritative for every hosting and operating decision that the
production-readiness records under infra/ must reflect (ADR-013; COMP-PLATFORM-OPERATIONS; AC-NFR-007-01,
AC-NFR-008-01, AC-NFR-009-01). Written by the Dariel managed services DevOps crew and signed off by the authorities
named in project-pack/project.yaml. Copy this file to `infra/PLATFORM-DECISIONS.md`, fill it, commit it, and only
then run `/execute TASK-16-PLATFORM-OPERATIONS`.

Rules for filling it in:

- Replace every `FILL IN`. A decision you cannot make yet is `UNDECIDED`, never a guess: the work item records an
  undecided value as unresolved, because the BRS forbids invented targets.
- Status is `DECIDED`, `PROPOSED` (awaiting the named authority) or `UNDECIDED`.
- No secret values anywhere in this file: names and locations only (a Key Vault name, never a password).
- Keep the headings and the table columns; the work item reads them as written.

Authorities (project-pack/project.yaml): product_domain = lebo; architecture = sasha; security_platform = wayne;
engineering = sasha; release = petre.

## 1. Hosting

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Azure subscription and resource group naming | FILL IN | FILL IN | security_platform |
| Region for all resources (data residency) | FILL IN (South Africa North unless decided otherwise) | FILL IN | security_platform |
| API hosting service (Container Apps or App Service) | FILL IN | FILL IN | architecture |
| Frontend hosting service (Static Web Apps or equivalent) | FILL IN | FILL IN | architecture |
| Database service and major version (Flexible Server, PostgreSQL 16 or later) | FILL IN | FILL IN | architecture |
| Container registry | FILL IN | FILL IN | security_platform |
| Environments and their names | FILL IN (for example demo, uat, prod) | FILL IN | release |
| Environments that run with ASPNETCORE_ENVIRONMENT=Production (every environment holding real employee data) | FILL IN | FILL IN | security_platform |
| Environments permitted to run Identity:Mode=Simulated (no real data, network-restricted) | FILL IN | FILL IN | security_platform |

## 2. Network exposure

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Edge (Front Door, Application Gateway, or the hosting service's own ingress) and web application firewall | FILL IN | FILL IN | security_platform |
| Exposure model: internet-facing behind Entra conditional access, or corporate network only | FILL IN | FILL IN | security_platform |
| Frontend and API under one hostname with /api/* routed to the API (no CORS), or separate origins with Cors:AllowedOrigins | FILL IN | FILL IN | architecture |
| Hostnames per environment and TLS certificate source | FILL IN | FILL IN | security_platform |
| Database network access (private endpoint or VNet integration; no public access) | FILL IN | FILL IN | security_platform |
| API egress allow-list (login.microsoftonline.com and the database only) | FILL IN | FILL IN | security_platform |
| Administrator path to the production database for legacy entry (bastion, VPN, or other) | FILL IN | FILL IN | security_platform |

## 3. Database protection and recovery

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| TLS mode the API connects with (Require, VerifyCA or VerifyFull) and certificate source | FILL IN | FILL IN | security_platform |
| Encryption at rest (service-managed or customer-managed keys) | FILL IN | FILL IN | security_platform |
| Backup retention (days) | FILL IN | FILL IN | release |
| Point-in-time recovery enabled | FILL IN | FILL IN | release |
| Geo-redundant backup | FILL IN | FILL IN | release |
| Recovery point objective (RPO) | FILL IN | FILL IN | release |
| Recovery time objective (RTO) | FILL IN | FILL IN | release |
| Restore test cadence and who performs it | FILL IN | FILL IN | release |
| Maintenance window | FILL IN | FILL IN | release |

## 4. Database principals

| Principal | Account or identity | Authentication (password in Key Vault, or Entra) | Status | Authority |
| --- | --- | --- | --- | --- |
| Schema owner, used only by the migration pipeline step | FILL IN | FILL IN | FILL IN | security_platform |
| API login, member of mentor_app only | FILL IN | FILL IN | FILL IN | security_platform |
| Talent Management administrators' personal logins, members of mentor_legacy_entry only, never of mentor_app | FILL IN (one per administrator) | FILL IN | FILL IN | security_platform |
| Database operator who grants legacy-entry membership and holds none | FILL IN | FILL IN | FILL IN | security_platform |

## 5. Secrets

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Key Vault name per environment | FILL IN | FILL IN | security_platform |
| Secrets held (expected: the API database password; the migration account password for the pipeline) | FILL IN | FILL IN | security_platform |
| How the API receives them (Key Vault references, mounted secrets, or other) | FILL IN | FILL IN | architecture |
| Rotation policy | FILL IN | FILL IN | security_platform |

## 6. Identity

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Entra tenant id (not a secret) | FILL IN | FILL IN | security_platform |
| Owner of the two app registrations (API and SPA) | FILL IN | FILL IN | security_platform |
| API registration: delegated scope name (the SPA requests api://<api-client-id>/<scope>) | FILL IN | FILL IN | security_platform |
| Entra group assigned the app role TalentManagement.Administrator | FILL IN | FILL IN | product_domain |
| SPA redirect URIs per environment | FILL IN | FILL IN | security_platform |
| Conditional access policy applied to the application | FILL IN | FILL IN | security_platform |

## 7. Production ownership and operator access

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Owning team and support hours | FILL IN | FILL IN | release |
| On-call and incident process | FILL IN | FILL IN | release |
| Change approval for production deployments | FILL IN | FILL IN | release |
| Who may reach the production database, how, and how that access is logged | FILL IN | FILL IN | security_platform |
| Access review cadence for database principals and Entra roles | FILL IN | FILL IN | security_platform |
| Statement that operator access confers no business-record permission (BRS role model) | Confirmed by design: the API's database role cannot enter legacy rows and operators hold no app role | DECIDED | architecture |

## 8. Retention and data protection (POPIA)

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Data classification of employee development records | FILL IN | FILL IN | product_domain |
| Retention period for application records and the audit trail (the schema never deletes; deletion or anonymisation needs a decision) | FILL IN | FILL IN | product_domain |
| Log retention period | FILL IN | FILL IN | security_platform |
| Data residency confirmation | FILL IN | FILL IN | security_platform |
| Access to exports (register CSV) outside the application | FILL IN | FILL IN | product_domain |

## 9. Observability

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Log destination (Log Analytics workspace or other) | FILL IN | FILL IN | release |
| Alerts (readiness probe failures at /health/ready, 5xx rate, database availability) | FILL IN | FILL IN | release |
| Dashboards or runbooks location | FILL IN | FILL IN | release |

## 10. Build and deployment

| Decision | Value | Status | Authority |
| --- | --- | --- | --- |
| Source repository location after transfer | FILL IN | FILL IN | engineering |
| CI/CD platform | FILL IN | FILL IN | release |
| Image build once, promoted through environments | FILL IN | FILL IN | release |
| Migration step: idempotent SQL script or dotnet-ef, run before the new API version receives traffic | FILL IN | FILL IN | architecture |
| Pipeline authentication to Azure (federated credentials, no stored cloud secret) | FILL IN | FILL IN | security_platform |
| Frontend build per environment (Vite bakes VITE_* values in at build time) | FILL IN | FILL IN | release |

## 11. Sign-off

| Authority | Identity | Sections | Date | Status |
| --- | --- | --- | --- | --- |
| security_platform | wayne | 1, 2, 3, 4, 5, 6, 7, 8, 10 | FILL IN | FILL IN |
| release | petre | 1, 3, 7, 9, 10 | FILL IN | FILL IN |
| architecture | sasha | 1, 2, 5, 7, 10 | FILL IN | FILL IN |
| product_domain | lebo | 6, 8 | FILL IN | FILL IN |
| engineering | sasha | 10 | FILL IN | FILL IN |
