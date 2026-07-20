import {
  MorphDBApiError,
  MorphDBNotFoundError,
  MorphDBValidationError,
  MorphDBAuthenticationError,
  MorphDBAuthorizationError,
  MorphDBConflictError,
} from './errors.js';
import type { MorphDBClientOptions } from './types.js';

export class HttpClient {
  private readonly baseUrl: string;
  private readonly options: Required<Pick<MorphDBClientOptions, 'timeout' | 'retryCount' | 'retryDelay'>> &
    MorphDBClientOptions;

  constructor(baseUrl: string, options: MorphDBClientOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.options = {
      timeout: options.timeout ?? 30000,
      retryCount: options.retryCount ?? 3,
      retryDelay: options.retryDelay ?? 1000,
      ...options,
    };
  }

  private getHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    };

    if (this.options.projectId) {
      headers['X-Project-Id'] = this.options.projectId;
    }

    if (this.options.apiKey) {
      headers['X-API-Key'] = this.options.apiKey;
    }

    if (this.options.jwtToken) {
      headers['Authorization'] = `Bearer ${this.options.jwtToken}`;
    }

    return headers;
  }

  async request<T>(
    method: string,
    path: string,
    body?: unknown,
    options?: { headers?: Record<string, string>; returnBlob?: boolean }
  ): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const headers = { ...this.getHeaders(), ...options?.headers };

    let lastError: Error | undefined;

    for (let attempt = 0; attempt <= this.options.retryCount; attempt++) {
      try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), this.options.timeout);

        const response = await fetch(url, {
          method,
          headers,
          body: body ? JSON.stringify(body) : undefined,
          signal: controller.signal,
        });

        clearTimeout(timeoutId);

        if (!response.ok) {
          const responseBody = await response.text();
          throw this.createError(response.status, responseBody, url);
        }

        if (options?.returnBlob) {
          return (await response.blob()) as T;
        }

        const text = await response.text();
        if (!text) {
          return undefined as T;
        }

        return JSON.parse(text) as T;
      } catch (error) {
        lastError = error as Error;

        // Don't retry on client errors
        if (error instanceof MorphDBApiError && error.statusCode >= 400 && error.statusCode < 500) {
          throw error;
        }

        // Wait before retry
        if (attempt < this.options.retryCount) {
          await this.delay(this.options.retryDelay * Math.pow(2, attempt));
        }
      }
    }

    throw lastError;
  }

  async get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }

  async put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }

  async patch<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PATCH', path, body);
  }

  async delete<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path);
  }

  async postFormData<T>(path: string, formData: FormData): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const headers = { ...this.getHeaders() };
    delete headers['Content-Type']; // Let browser set it for FormData

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.options.timeout);

    const response = await fetch(url, {
      method: 'POST',
      headers,
      body: formData,
      signal: controller.signal,
    });

    clearTimeout(timeoutId);

    if (!response.ok) {
      const responseBody = await response.text();
      throw this.createError(response.status, responseBody, url);
    }

    return response.json() as Promise<T>;
  }

  async getBlob(path: string): Promise<Blob> {
    return this.request<Blob>('GET', path, undefined, { returnBlob: true });
  }

  private createError(status: number, responseBody: string, url: string): MorphDBApiError {
    switch (status) {
      case 404:
        return new MorphDBNotFoundError(`Resource not found: ${url}`, responseBody);
      case 400:
        return new MorphDBValidationError('Validation failed', undefined, responseBody);
      case 401:
        return new MorphDBAuthenticationError('Authentication required', responseBody);
      case 403:
        return new MorphDBAuthorizationError('Access denied', responseBody);
      case 409:
        return new MorphDBConflictError('Resource conflict', responseBody);
      default:
        return new MorphDBApiError(`Request failed with status ${status}`, status, undefined, responseBody);
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  setProjectId(projectId: string): void {
    this.options.projectId = projectId;
  }

  setApiKey(apiKey: string): void {
    this.options.apiKey = apiKey;
  }

  setJwtToken(jwtToken: string): void {
    this.options.jwtToken = jwtToken;
  }
}
