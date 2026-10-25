import { Link, Navigate, Route, Routes } from 'react-router-dom';
import { OrganizationLayout, RequireAuth } from './components/Layout';
import { AcceptInvitePage } from './pages/AcceptInvitePage';
import { AuditPage } from './pages/AuditPage';
import { LoginPage } from './pages/LoginPage';
import { MembersPage } from './pages/MembersPage';
import { OrganizationsPage } from './pages/OrganizationsPage';
import { ProjectPage } from './pages/ProjectPage';
import { ProjectsPage } from './pages/ProjectsPage';
import { RegisterPage } from './pages/RegisterPage';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />

      {/* Public: the invitee may not have an account yet. */}
      <Route path="/invitations/accept" element={<AcceptInvitePage />} />

      <Route element={<RequireAuth />}>
        <Route path="/" element={<Navigate to="/orgs" replace />} />
        <Route path="/orgs" element={<OrganizationsPage />} />
        <Route path="/orgs/:organizationId" element={<OrganizationLayout />}>
          <Route index element={<ProjectsPage />} />
          <Route path="projects/:projectId" element={<ProjectPage />} />
          <Route path="members" element={<MembersPage />} />
          <Route path="audit" element={<AuditPage />} />
        </Route>
      </Route>

      <Route
        path="*"
        element={
          <div className="mx-auto max-w-md p-10 text-center">
            <h1 className="text-lg font-semibold">Page not found</h1>
            <Link to="/" className="mt-2 inline-block text-sm text-[var(--color-accent)] hover:underline">
              Go to your organizations
            </Link>
          </div>
        }
      />
    </Routes>
  );
}
