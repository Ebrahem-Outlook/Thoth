import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ChatResponseDto,
  ClientConfig,
  Conversation,
  ConversationDetail,
  MemoryRecord,
  SystemStatus,
  ToolDefinition,
  WorkspaceSummary,
} from '../models/thoth.models';

@Injectable({ providedIn: 'root' })
export class ThothApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  getClientConfig(): Observable<ClientConfig> {
    return this.http.get<ClientConfig>(`${this.baseUrl}/api/client-config`);
  }

  listConversations(query = '', includeArchived = false): Observable<Conversation[]> {
    let params = new HttpParams().set('includeArchived', includeArchived);
    if (query.trim()) {
      params = params.set('query', query.trim());
    }

    return this.http.get<Conversation[]>(`${this.baseUrl}/api/conversations`, { params });
  }

  createConversation(title = 'New chat'): Observable<Conversation> {
    return this.http.post<Conversation>(`${this.baseUrl}/api/conversations`, { title });
  }

  getConversation(id: string): Observable<ConversationDetail> {
    return this.http.get<ConversationDetail>(`${this.baseUrl}/api/conversations/${id}`);
  }

  updateConversation(
    id: string,
    patch: Partial<Pick<Conversation, 'title' | 'isPinned' | 'isArchived' | 'project'>>,
  ): Observable<Conversation> {
    return this.http.patch<Conversation>(`${this.baseUrl}/api/conversations/${id}`, patch);
  }

  deleteConversation(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/conversations/${id}`);
  }

  sendMessage(
    content: string,
    conversationId: string | null,
    files: File[],
    options: { model?: string; useTools?: boolean; maxSteps?: number } = {},
  ): Observable<ChatResponseDto> {
    const form = new FormData();
    form.set('content', content);
    if (conversationId) {
      form.set('conversationId', conversationId);
    }

    if (options.model) {
      form.set('model', options.model);
    }

    if (options.useTools !== undefined) {
      form.set('useTools', String(options.useTools));
    }

    if (options.maxSteps !== undefined) {
      form.set('maxSteps', String(options.maxSteps));
    }

    for (const file of files) {
      form.append('files', file, file.name);
    }

    const url = conversationId
      ? `${this.baseUrl}/api/conversations/${conversationId}/messages`
      : `${this.baseUrl}/api/chat`;

    return this.http.post<ChatResponseDto>(url, form);
  }

  listTools(): Observable<ToolDefinition[]> {
    return this.http.get<ToolDefinition[]>(`${this.baseUrl}/api/tools`);
  }

  getSystemStatus(): Observable<SystemStatus> {
    return this.http.get<SystemStatus>(`${this.baseUrl}/api/system/status`);
  }

  getWorkspaceSummary(): Observable<WorkspaceSummary> {
    return this.http.get<WorkspaceSummary>(`${this.baseUrl}/api/workspace/summary`);
  }

  searchMemory(query: string, scope = 'project', limit = 12): Observable<MemoryRecord[]> {
    let params = new HttpParams().set('query', query || '').set('limit', limit);
    if (scope.trim()) {
      params = params.set('scope', scope.trim());
    }

    return this.http.get<MemoryRecord[]>(`${this.baseUrl}/api/memory/search`, { params });
  }

  addMemory(content: string, scope = 'project'): Observable<MemoryRecord> {
    return this.http.post<MemoryRecord>(`${this.baseUrl}/api/memory`, { content, scope });
  }

  attachmentDownloadUrl(id: string): string {
    return `${this.baseUrl}/api/attachments/${id}/download`;
  }
}
