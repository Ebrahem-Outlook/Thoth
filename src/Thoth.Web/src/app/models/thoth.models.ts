export type ChatRole = 'System' | 'User' | 'Assistant' | 'Tool' | 0 | 1 | 2 | 3;

export interface Conversation {
  id: string;
  title: string;
  project?: string | null;
  isPinned: boolean;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface ConversationAttachment {
  id: string;
  conversationId?: string | null;
  messageId?: string | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  storagePath: string;
  createdAt: string;
}

export interface ConversationMessage {
  id: string;
  conversationId: string;
  role: ChatRole;
  content: string;
  createdAt: string;
  attachments: ConversationAttachment[];
  intent?: string | null;
  metadataJson?: string | null;
}

export interface ConversationDetail {
  conversation: Conversation;
  messages: ConversationMessage[];
}

export interface UnderstandingResult {
  intent: string;
  topic: string;
  language: string;
  requiresTools: boolean;
  requiresVision: boolean;
  isLongContext: boolean;
  confidence: number;
  summary: string;
}

export interface AgentStep {
  index: number;
  thought: string;
  invocation?: {
    toolName: string;
    arguments: Record<string, string | null>;
  } | null;
  result?: {
    toolName: string;
    succeeded: boolean;
    content: string;
    metadata: Record<string, string>;
  } | null;
  startedAt: string;
  completedAt: string;
}

export interface AgentRun {
  runId: string;
  goal: string;
  workingDirectory: string;
  finalAnswer: string;
  succeeded: boolean;
  steps: AgentStep[];
}

export interface ChatResponseDto {
  conversationId: string;
  userMessageId: string;
  assistantMessageId: string;
  assistantContent: string;
  understanding: UnderstandingResult;
  agentRun?: AgentRun | null;
  diagnostics?: DeveloperDiagnostics | null;
}

export interface DeveloperDiagnostics {
  understanding: UnderstandingResult;
  agentRun?: AgentRun | null;
  toolsUsed: boolean;
  planSource?: string | null;
}

export interface ToolDefinition {
  name: string;
  description: string;
  parameters: Array<{
    name: string;
    description: string;
    required: boolean;
    type: string;
  }>;
}

export interface ClientConfig {
  provider: string;
  model: string;
  allowShell: boolean;
  maxAgentSteps: number;
  features: string[];
}

export interface WorkspaceEndpoint {
  method: string;
  route: string;
  sourceFile: string;
}

export interface WorkspaceSummary {
  root: string;
  generatedAt: string;
  fileCount: number;
  directoryCount: number;
  dotNetProjects: string[];
  packageFiles: string[];
  topLevelEntries: string[];
  endpoints: WorkspaceEndpoint[];
}

export interface SystemStatus {
  runtimeMode: string;
  model: string;
  selfContainedOnly: boolean;
  shellEnabled: boolean;
  toolCount: number;
  conversationCount: number;
  memoryCount: number;
  time: string;
  modelStatus: string;
  modelStatusReasons: string[];
}

export interface MemoryRecord {
  id: string;
  scope: string;
  content: string;
  createdAt: string;
  metadata: Record<string, string>;
}
