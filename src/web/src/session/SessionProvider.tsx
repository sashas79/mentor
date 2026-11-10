import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { ApiProblem, expect, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";

export type Session = Schemas["Session"];
export type FunctionalRole = Schemas["FunctionalRole"];

export interface SessionState {
  session?: Session;
  loading: boolean;
  problem?: ApiProblem;
  /** Whether the caller's roles reach the action at all, as the server reports (AC-FR-003-01). A record may still be refused. */
  can(permission: string): boolean;
  hasRole(role: FunctionalRole): boolean;
  /** Whether the record names the caller, by their employee record. */
  isMe(employeeId: string | null | undefined): boolean;
  refresh(): Promise<void>;
}

const SessionContext = createContext<SessionState | null>(null);

export function useSession(): SessionState {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error("useSession is used outside SessionProvider.");
  }

  return session;
}

/**
 * The signed-in caller as the server sees them (GET /api/session): their employee record, roles and the permissions
 * those roles reach. The employee's own profile is opened first, which records the employee on first access. Nothing
 * here decides what the caller may do: screens offer what the server reports and the server answers every request.
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const { api, phase } = useIdentity();
  const [session, setSession] = useState<Session>();
  const [loading, setLoading] = useState(false);
  const [problem, setProblem] = useState<ApiProblem>();

  const refresh = useCallback(async () => {
    setLoading(true);
    setProblem(undefined);
    try {
      // The first access to the employee's own profile records them (COMP-EMPLOYEE-PROFILE). An identity with no display
      // name gets "No profile yet" here and saves the profile to be recorded; the session is read either way.
      await expect(api.GET("/api/employees/me")).catch((error: unknown) => {
        if (!(error instanceof ApiProblem && error.status === 404)) throw error;
      });
      setSession(await expect(api.GET("/api/session")));
    } catch (error) {
      setSession(undefined);
      setProblem(error instanceof ApiProblem ? error : new ApiProblem(0, "The mentorship service could not be reached", undefined, {}));
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    if (phase === "signedIn") {
      void refresh();
    } else {
      setSession(undefined);
    }
  }, [phase, refresh]);

  const value = useMemo<SessionState>(
    () => ({
      session,
      loading,
      problem,
      can: (permission) => session?.permissions.includes(permission) ?? false,
      hasRole: (role) => session?.roles.includes(role) ?? false,
      isMe: (employeeId) => Boolean(employeeId && session?.employeeId && employeeId === session.employeeId),
      refresh,
    }),
    [session, loading, problem, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
