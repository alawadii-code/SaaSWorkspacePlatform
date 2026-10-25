/**
 * Response shapes mirroring the API's DTOs. Enums arrive as names, because the
 * API serializes them with a string converter.
 */

export type OrganizationRole = 'Viewer' | 'Member' | 'Admin' | 'Owner';

export type ProjectStatus = 'Active' | 'Archived';

export type TaskState = 'Todo' | 'InProgress' | 'Blocked' | 'Done';

export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent';

export type InvitationStatus = 'Pending' | 'Accepted' | 'Revoked' | 'Expired';

export type InvitationDeliveryStatus = 'Pending' | 'Sent' | 'Failed';

/**
 * Permission names as the server spells them. Used only to decide what to
 * render: every request is authorized again server-side.
 */
export type Permission =
  | 'OrganizationRead'
  | 'OrganizationUpdate'
  | 'OrganizationDelete'
  | 'MemberRead'
  | 'MemberInvite'
  | 'MemberRoleUpdate'
  | 'MemberRemove'
  | 'InvitationRead'
  | 'InvitationRevoke'
  | 'ProjectRead'
  | 'ProjectCreate'
  | 'ProjectUpdate'
  | 'ProjectDelete'
  | 'TaskRead'
  | 'TaskCreate'
  | 'TaskUpdateAny'
  | 'TaskUpdateOwn'
  | 'TaskAssign'
  | 'TaskDelete'
  | 'AuditRead';

export interface User {
  id: string;
  email: string;
  displayName: string;
  createdAt: string;
}

export interface AuthResult {
  accessToken: string;
  tokenType: string;
  expiresInSeconds: number;
  refreshToken: string | null;
  user: User;
}

export interface OrganizationMembership {
  organizationId: string;
  name: string;
  slug: string;
  role: OrganizationRole;
  permissions: Permission[];
  joinedAt: string;
}

export interface MeResponse {
  user: User;
  organizations: OrganizationMembership[];
}

export interface Organization {
  id: string;
  name: string;
  slug: string;
  callerRole: OrganizationRole;
  callerPermissions: Permission[];
  memberCount: number;
  projectCount: number;
  createdAt: string;
}

export interface Member {
  membershipId: string;
  userId: string;
  email: string;
  displayName: string;
  role: OrganizationRole;
  joinedAt: string;
  isYou: boolean;
}

export interface Project {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  status: ProjectStatus;
  taskCount: number;
  openTaskCount: number;
  createdByUserId: string;
  createdAt: string;
  archivedAt: string | null;
}

export interface WorkTask {
  id: string;
  projectId: string;
  projectName: string;
  title: string;
  description: string | null;
  status: TaskState;
  priority: TaskPriority;
  assigneeUserId: string | null;
  assigneeName: string | null;
  createdByUserId: string;
  dueAt: string | null;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface Invitation {
  id: string;
  email: string;
  role: OrganizationRole;
  status: InvitationStatus;
  deliveryStatus: InvitationDeliveryStatus;
  deliveryAttempts: number;
  deliveryError: string | null;
  expiresAt: string;
  createdAt: string;
  acceptedAt: string | null;
}

export interface InvitationPreview {
  organizationName: string;
  email: string;
  role: OrganizationRole;
  expiresAt: string;
  isValid: boolean;
  reason: string | null;
}

export interface AcceptInvitationResult {
  organizationId: string;
  organizationName: string;
  organizationSlug: string;
  role: OrganizationRole;
}

export interface AuditEntry {
  id: string;
  action: string;
  resourceType: string;
  resourceId: string | null;
  actorUserId: string | null;
  actorEmail: string | null;
  metadata: unknown;
  ipAddress: string | null;
  correlationId: string | null;
  createdAt: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}
