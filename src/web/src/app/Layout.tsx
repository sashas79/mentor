import type { ReactNode } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { label } from "@/ui/labels";

interface NavItem {
  to: string;
  text: string;
  /** The permission the server reports the caller's roles reach; the item is offered only then. */
  permission: string;
}

/**
 * Role-appropriate navigation (ADR-012, AC-FR-003-01, AC-FR-003-02). Every area is offered by the permissions the
 * server reports for the caller: an employee who is both a mentor and a protégé sees both, Talent Management sees its
 * administration area, and nobody else does. Hiding an item protects nothing: the server refuses what it does not permit.
 */
const areas: { title: string; items: NavItem[] }[] = [
  {
    title: "My mentoring",
    items: [
      { to: "/", text: "Overview", permission: "session.read" },
      { to: "/notifications", text: "Notifications", permission: "notification.read" },
      { to: "/profile", text: "My profile", permission: "employee-profile.read" },
    ],
  },
  {
    title: "Find a mentor",
    items: [
      { to: "/mentors", text: "Mentors", permission: "mentor-discovery.list" },
      { to: "/requests", text: "My requests", permission: "mentorship-request.create" },
      { to: "/candidates", text: "Pairings and agreements", permission: "candidate.read" },
      { to: "/relationships", text: "My mentorships", permission: "relationship.read" },
    ],
  },
  {
    title: "Mentoring others",
    items: [{ to: "/mentor-profile", text: "Mentor profile", permission: "mentor-application.create" }],
  },
  {
    title: "Talent Management",
    items: [
      { to: "/admin/applications", text: "Mentor applications", permission: "mentor-application.list-pending" },
      { to: "/admin/candidates", text: "Pairings and formation", permission: "formation.decide" },
      { to: "/admin/register", text: "Relationship register", permission: "register.read" },
      { to: "/admin/goals", text: "Goals and progress", permission: "register.read" },
    ],
  },
];

export function Layout() {
  const { signOut } = useIdentity();
  const { session, can } = useSession();

  return (
    <div className="shell">
      <nav className="shell-nav" aria-label="Main">
        <div className="who">
          <strong>{session?.displayName}</strong>
          <div>{session?.roles.map(label).join(" · ")}</div>
          <button type="button" className="secondary" onClick={() => void signOut()}>
            Sign out
          </button>
        </div>
        {areas.map((area) => {
          const items = area.items.filter((item) => can(item.permission));
          if (items.length === 0) return null;
          return (
            <div key={area.title}>
              <h2>{area.title}</h2>
              <ul>
                {items.map((item) => (
                  <li key={item.to}>
                    <NavLink to={item.to} end={item.to === "/"}>
                      {item.text}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          );
        })}
      </nav>
      <main className="shell-main">
        <Outlet />
      </main>
    </div>
  );
}

/** A screen of an area the caller's roles do not reach: the server would refuse it, so it is not offered (AC-FR-060-02). */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { can } = useSession();
  if (!can(permission)) {
    return (
      <div className="message message-problem" role="alert">
        <strong>This area is not available to you.</strong>
        <p>Your role does not include it. The service refuses it too.</p>
      </div>
    );
  }

  return <>{children}</>;
}
