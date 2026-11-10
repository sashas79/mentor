import { Route, Routes } from "react-router-dom";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { AdminCandidates } from "@/screens/admin/AdminCandidates";
import { GoalsOverview } from "@/screens/admin/GoalsOverview";
import { MentorApplications } from "@/screens/admin/MentorApplications";
import { Register } from "@/screens/admin/Register";
import { CandidateDetail } from "@/screens/candidates/CandidateDetail";
import { Candidates } from "@/screens/candidates/Candidates";
import { DraftScreen } from "@/screens/candidates/DraftScreen";
import { FormationScreen } from "@/screens/candidates/FormationScreen";
import { Home } from "@/screens/Home";
import { MentorDetail } from "@/screens/mentors/MentorDetail";
import { MentorProfile } from "@/screens/mentors/MentorProfile";
import { Mentors } from "@/screens/mentors/Mentors";
import { Notifications } from "@/screens/Notifications";
import { EmployeeProfile } from "@/screens/EmployeeProfile";
import { RelationshipDetail } from "@/screens/relationships/RelationshipDetail";
import { GoalRedirect, Relationships } from "@/screens/relationships/Relationships";
import { RequestDetail } from "@/screens/requests/RequestDetail";
import { Requests } from "@/screens/requests/Requests";
import { ProblemMessage } from "@/ui/components";
import { Layout, RequirePermission } from "./Layout";
import { SignIn } from "./SignIn";
import { SimulatedBanner } from "./SimulatedBanner";

/** The application: sign-in until the server knows the caller, then the screens their roles reach. */
export function App() {
  return (
    <>
      <SimulatedBanner />
      <Signed />
    </>
  );
}

function Signed() {
  const { phase, signOut } = useIdentity();
  const { session, problem } = useSession();

  if (phase !== "signedIn") return <SignIn />;
  if (problem && !session) {
    return (
      <main className="shell-main">
        <ProblemMessage problem={problem} />
        <button type="button" className="secondary" onClick={() => void signOut()}>
          Sign out
        </button>
      </main>
    );
  }

  if (!session) {
    return (
      <main className="shell-main">
        <p role="status">Signing in…</p>
      </main>
    );
  }

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="profile" element={<EmployeeProfile />} />
        <Route path="notifications" element={<Notifications />} />
        <Route path="mentor-profile" element={<MentorProfile />} />
        <Route path="mentors" element={<Mentors />} />
        <Route path="mentors/:profileId" element={<MentorDetail />} />
        <Route path="requests" element={<Requests />} />
        <Route path="requests/:requestId" element={<RequestDetail />} />
        <Route path="candidates" element={<Candidates />} />
        <Route path="candidates/:candidateId" element={<CandidateDetail />} />
        <Route path="candidates/:candidateId/draft" element={<DraftScreen />} />
        <Route path="candidates/:candidateId/formation" element={<FormationScreen />} />
        <Route path="relationships" element={<Relationships />} />
        <Route path="relationships/:relationshipId" element={<RelationshipDetail />} />
        <Route path="goals/:goalId" element={<GoalRedirect />} />
        <Route
          path="admin/applications"
          element={
            <RequirePermission permission="mentor-application.list-pending">
              <MentorApplications />
            </RequirePermission>
          }
        />
        <Route
          path="admin/candidates"
          element={
            <RequirePermission permission="formation.decide">
              <AdminCandidates />
            </RequirePermission>
          }
        />
        <Route
          path="admin/register"
          element={
            <RequirePermission permission="register.read">
              <Register />
            </RequirePermission>
          }
        />
        <Route
          path="admin/goals"
          element={
            <RequirePermission permission="register.read">
              <GoalsOverview />
            </RequirePermission>
          }
        />
        <Route path="*" element={<p>There is nothing at this address.</p>} />
      </Route>
    </Routes>
  );
}
