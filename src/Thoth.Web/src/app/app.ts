import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  LucideArchive,
  LucideBot,
  LucideBrain,
  LucideChevronLeft,
  LucideChevronRight,
  LucideCopy,
  LucideDownload,
  LucideFile,
  LucideImage,
  LucideInfo,
  LucideMenu,
  LucideMessageSquare,
  LucideMic,
  LucideMoreVertical,
  LucidePanelRight,
  LucidePaperclip,
  LucidePin,
  LucidePlus,
  LucideRefreshCw,
  LucideSearch,
  LucideSend,
  LucideSettings,
  LucideSparkles,
  LucideTrash2,
  LucideWrench,
  LucideX,
} from '@lucide/angular';
import {
  AgentRun,
  ChatResponseDto,
  ClientConfig,
  Conversation,
  ConversationDetail,
  ConversationMessage,
  MemoryRecord,
  SystemStatus,
  ToolDefinition,
  WorkspaceSummary,
} from './models/thoth.models';
import { MarkdownPipe } from './pipes/markdown.pipe';
import { ThothApiService } from './services/thoth-api.service';

interface PendingFile {
  file: File;
  previewUrl?: string;
  kind: 'image' | 'file';
}

type SidePanel = 'tools' | 'memory' | 'settings' | 'run' | 'workspace';
type NormalizedRole = 'user' | 'assistant' | 'system' | 'tool';

@Component({
  selector: 'app-root',
  imports: [
    CommonModule,
    FormsModule,
    MarkdownPipe,
    LucideArchive,
    LucideBot,
    LucideBrain,
    LucideChevronLeft,
    LucideChevronRight,
    LucideCopy,
    LucideDownload,
    LucideFile,
    LucideImage,
    LucideInfo,
    LucideMenu,
    LucideMessageSquare,
    LucideMic,
    LucideMoreVertical,
    LucidePanelRight,
    LucidePaperclip,
    LucidePin,
    LucidePlus,
    LucideRefreshCw,
    LucideSearch,
    LucideSend,
    LucideSettings,
    LucideSparkles,
    LucideTrash2,
    LucideWrench,
    LucideX,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  @ViewChild('fileInput') fileInput?: ElementRef<HTMLInputElement>;
  @ViewChild('messageInput') messageInput?: ElementRef<HTMLTextAreaElement>;

  private readonly api = inject(ThothApiService);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private copyTimer?: ReturnType<typeof setTimeout>;

  readonly conversations = signal<Conversation[]>([]);
  readonly selectedConversation = signal<ConversationDetail | null>(null);
  readonly transientMessages = signal<ConversationMessage[]>([]);
  readonly config = signal<ClientConfig | null>(null);
  readonly tools = signal<ToolDefinition[]>([]);
  readonly memories = signal<MemoryRecord[]>([]);
  readonly workspaceSummary = signal<WorkspaceSummary | null>(null);
  readonly systemStatus = signal<SystemStatus | null>(null);
  readonly lastRun = signal<AgentRun | null>(null);
  readonly activePanel = signal<SidePanel>('run');
  readonly sidebarOpen = signal(true);
  readonly rightPanelOpen = signal(false);
  readonly busy = signal(false);
  readonly booting = signal(true);
  readonly error = signal('');
  readonly query = signal('');
  readonly memoryQuery = signal('');
  readonly composerText = signal('');
  readonly pendingFiles = signal<PendingFile[]>([]);
  readonly copiedMessageId = signal<string | null>(null);
  readonly draggingFiles = signal(false);
  readonly selectedModel = signal('');
  readonly useTools = signal(true);
  readonly maxSteps = signal(8);

  readonly pinnedConversations = computed(() => this.conversations().filter((item) => item.isPinned));
  readonly recentConversations = computed(() => this.conversations().filter((item) => !item.isPinned));
  readonly messages = computed(() => [
    ...(this.selectedConversation()?.messages ?? []),
    ...this.transientMessages(),
  ]);
  readonly activeConversationId = computed(() => this.selectedConversation()?.conversation.id ?? null);
  readonly currentTitle = computed(() => this.selectedConversation()?.conversation?.title || 'New conversation');
  readonly statusLine = computed(() => {
    const status = this.systemStatus();
    if (!status) {
      return this.config()?.provider ? `${this.config()?.provider} runtime` : 'connecting';
    }

    return `${status.runtimeMode} - ${status.toolCount} tools - shell ${status.shellEnabled ? 'on' : 'off'}`;
  });

  ngOnInit(): void {
    if (this.isCompactViewport()) {
      this.sidebarOpen.set(false);
      this.rightPanelOpen.set(false);
    }

    this.refreshAll();
  }

  ngOnDestroy(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }

    if (this.copyTimer) {
      clearTimeout(this.copyTimer);
    }

    for (const file of this.pendingFiles()) {
      if (file.previewUrl) {
        URL.revokeObjectURL(file.previewUrl);
      }
    }
  }

  refreshAll(): void {
    this.booting.set(true);
    this.error.set('');

    this.api.getClientConfig().subscribe({
      next: (config) => {
        this.config.set(config);
        this.selectedModel.set(config.model);
        this.maxSteps.set(config.maxAgentSteps);
      },
      error: () => this.error.set('Backend is offline. Start Thoth.Api on http://127.0.0.1:5055.'),
    });

    this.loadConversations();
    this.loadTools();
    this.loadWorkspace();
    this.searchMemory(' ');
    this.booting.set(false);
  }

  loadConversations(): void {
    this.api.listConversations(this.query()).subscribe({
      next: (items) => {
        this.conversations.set(items);
        if (!this.selectedConversation() && items[0]) {
          this.openConversation(items[0].id);
        }
      },
      error: () => this.error.set('Could not load conversations.'),
    });
  }

  openConversation(id: string): void {
    this.api.getConversation(id).subscribe({
      next: (detail) => {
        this.selectedConversation.set(detail);
        this.transientMessages.set([]);
        this.lastRun.set(null);
        if (this.isCompactViewport()) {
          this.sidebarOpen.set(false);
        }
        queueMicrotask(() => this.scrollToBottom());
      },
      error: () => this.error.set('Could not open this conversation.'),
    });
  }

  newConversation(): void {
    this.selectedConversation.set(null);
    this.transientMessages.set([]);
    this.lastRun.set(null);
    this.composerText.set('');
    this.clearFiles();
    if (this.isCompactViewport()) {
      this.sidebarOpen.set(false);
    }
    queueMicrotask(() => {
      this.autoResizeComposer();
      this.messageInput?.nativeElement.focus();
    });
  }

  togglePinned(conversation: Conversation, event: Event): void {
    event.stopPropagation();
    this.api.updateConversation(conversation.id, { isPinned: !conversation.isPinned }).subscribe({
      next: () => this.loadConversations(),
      error: () => this.error.set('Could not update pin.'),
    });
  }

  archiveConversation(conversation: Conversation, event?: Event): void {
    event?.stopPropagation();
    this.api.updateConversation(conversation.id, { isArchived: true }).subscribe({
      next: () => {
        if (this.activeConversationId() === conversation.id) {
          this.selectedConversation.set(null);
        }
        this.loadConversations();
      },
      error: () => this.error.set('Could not archive conversation.'),
    });
  }

  deleteConversation(conversation: Conversation, event?: Event): void {
    event?.stopPropagation();
    this.api.deleteConversation(conversation.id).subscribe({
      next: () => {
        if (this.activeConversationId() === conversation.id) {
          this.selectedConversation.set(null);
        }
        this.loadConversations();
      },
      error: () => this.error.set('Could not delete conversation.'),
    });
  }

  loadTools(): void {
    this.api.listTools().subscribe({
      next: (tools) => this.tools.set(tools),
      error: () => this.error.set('Could not load tools.'),
    });
  }

  loadWorkspace(): void {
    this.api.getWorkspaceSummary().subscribe({
      next: (summary) => this.workspaceSummary.set(summary),
      error: () => this.error.set('Could not load workspace summary.'),
    });

    this.api.getSystemStatus().subscribe({
      next: (status) => this.systemStatus.set(status),
      error: () => this.error.set('Could not load system status.'),
    });
  }

  searchMemory(query = this.memoryQuery()): void {
    this.memoryQuery.set(query);
    this.api.searchMemory(query || '', 'project').subscribe({
      next: (memories) => this.memories.set(memories),
      error: () => this.error.set('Could not search memory.'),
    });
  }

  onSearchChange(value: string): void {
    this.query.set(value);
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }

    this.searchTimer = setTimeout(() => this.loadConversations(), 160);
  }

  onComposerChange(value: string): void {
    this.composerText.set(value);
    queueMicrotask(() => this.autoResizeComposer());
  }

  send(): void {
    const content = this.composerText().trim();
    const files = this.pendingFiles().map((item) => item.file);
    if ((!content && files.length === 0) || this.busy()) {
      return;
    }

    const optimisticUser: ConversationMessage = {
      id: crypto.randomUUID(),
      conversationId: this.activeConversationId() ?? 'pending',
      role: 'User',
      content,
      createdAt: new Date().toISOString(),
      attachments: files.map((file) => ({
        id: crypto.randomUUID(),
        conversationId: null,
        messageId: null,
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        sizeBytes: file.size,
        storagePath: '',
        createdAt: new Date().toISOString(),
      })),
    };

    this.transientMessages.set([optimisticUser]);

    this.busy.set(true);
    this.error.set('');
    this.composerText.set('');
    this.clearFiles();
    queueMicrotask(() => {
      this.autoResizeComposer();
      this.scrollToBottom('smooth');
    });

    this.api
      .sendMessage(content, this.activeConversationId(), files, {
        model: this.selectedModel() || this.config()?.model,
        useTools: this.useTools(),
        maxSteps: this.maxSteps(),
      })
      .subscribe({
        next: (response) => this.applyChatResponse(response),
        error: (err) => {
          this.error.set(err?.error?.error ?? 'Message failed.');
          this.busy.set(false);
        },
      });
  }

  applyChatResponse(response: ChatResponseDto): void {
    this.transientMessages.set([]);
    this.lastRun.set(response.agentRun ?? null);
    this.activePanel.set(response.agentRun ? 'run' : 'memory');
    this.busy.set(false);
    this.loadConversations();
    this.openConversation(response.conversationId);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) {
      return;
    }

    this.addFiles(Array.from(input.files));
    input.value = '';
  }

  onPaste(event: ClipboardEvent): void {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (!files.length) {
      return;
    }

    event.preventDefault();
    this.addFiles(files);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer?.types?.includes('Files')) {
      this.draggingFiles.set(true);
    }
  }

  onDragLeave(event: DragEvent): void {
    if (event.currentTarget === event.target) {
      this.draggingFiles.set(false);
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.draggingFiles.set(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length) {
      this.addFiles(files);
    }
  }

  addFiles(files: File[]): void {
    const next = [...this.pendingFiles()];
    for (const file of files) {
      const isImage = file.type.startsWith('image/');
      next.push({
        file,
        kind: isImage ? 'image' : 'file',
        previewUrl: isImage ? URL.createObjectURL(file) : undefined,
      });
    }

    this.pendingFiles.set(next);
  }

  removeFile(index: number): void {
    const files = [...this.pendingFiles()];
    const [removed] = files.splice(index, 1);
    if (removed?.previewUrl) {
      URL.revokeObjectURL(removed.previewUrl);
    }
    this.pendingFiles.set(files);
  }

  clearFiles(revoke = true): void {
    if (revoke) {
      for (const file of this.pendingFiles()) {
        if (file.previewUrl) {
          URL.revokeObjectURL(file.previewUrl);
        }
      }
    }
    this.pendingFiles.set([]);
  }

  setPanel(panel: SidePanel): void {
    this.activePanel.set(panel);
    this.rightPanelOpen.set(true);
    if (this.isCompactViewport()) {
      this.sidebarOpen.set(false);
    }
  }

  copyMessage(id: string, content: string): void {
    void navigator.clipboard?.writeText(content);
    this.copiedMessageId.set(id);

    if (this.copyTimer) {
      clearTimeout(this.copyTimer);
    }

    this.copyTimer = setTimeout(() => this.copiedMessageId.set(null), 1300);
  }

  startDictation(): void {
    const SpeechRecognition =
      (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (!SpeechRecognition) {
      this.error.set('Voice input is not available in this browser.');
      return;
    }

    const recognition = new SpeechRecognition();
    recognition.lang = 'ar-EG';
    recognition.interimResults = false;
    recognition.onresult = (event: any) => {
      const text = event.results?.[0]?.[0]?.transcript ?? '';
      this.composerText.set(`${this.composerText()} ${text}`.trim());
      queueMicrotask(() => {
        this.autoResizeComposer();
        this.messageInput?.nativeElement.focus();
      });
    };
    recognition.start();
  }

  messageRole(message: ConversationMessage): NormalizedRole {
    if (message.role === 1) {
      return 'user';
    }

    if (message.role === 2) {
      return 'assistant';
    }

    if (message.role === 3) {
      return 'tool';
    }

    if (message.role === 0) {
      return 'system';
    }

    const role = String(message.role).toLowerCase();
    if (role.includes('user')) {
      return 'user';
    }

    if (role.includes('tool')) {
      return 'tool';
    }

    if (role.includes('system')) {
      return 'system';
    }

    return 'assistant';
  }

  messageLabel(message: ConversationMessage): string {
    const role = this.messageRole(message);
    if (role === 'user') {
      return 'You';
    }

    if (role === 'tool') {
      return 'Tool';
    }

    if (role === 'system') {
      return 'System';
    }

    return 'Thoth';
  }

  messageInitial(message: ConversationMessage): string {
    return this.messageRole(message) === 'user' ? 'Y' : 'T';
  }

  formatIntent(intent?: string | null): string {
    if (!intent) {
      return '';
    }

    return intent
      .replace(/_/g, ' ')
      .replace(/\b\w/g, (letter) => letter.toUpperCase());
  }

  memoryPreview(content: string): string {
    const normalized = content.replace(/\s+/g, ' ').trim();
    return normalized.length <= 420 ? normalized : `${normalized.slice(0, 420)}...`;
  }

  downloadAttachment(id: string): string {
    return this.api.attachmentDownloadUrl(id);
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  private autoResizeComposer(): void {
    const textarea = this.messageInput?.nativeElement;
    if (!textarea) {
      return;
    }

    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(textarea.scrollHeight, 180)}px`;
  }

  private scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    const surface = document.querySelector('.messages');
    surface?.scrollTo({ top: surface.scrollHeight, behavior });
  }

  private isCompactViewport(): boolean {
    return typeof window !== 'undefined' && window.matchMedia('(max-width: 960px)').matches;
  }
}
