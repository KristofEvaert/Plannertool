import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { UserDto } from '@models/user.model';
import type { AssignRolesRequest, CreateUserRequest } from '@models/user.model';

@Injectable({ providedIn: 'root' })
export class UsersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/users`;

  getMe(): Observable<UserDto> {
    return this.http.get<UserDto>(`${this.baseUrl}/me`);
  }

  getUsers(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(this.baseUrl);
  }

  createUser(request: CreateUserRequest): Observable<UserDto> {
    return this.http.post<UserDto>(this.baseUrl, request);
  }

  assignRoles(request: AssignRolesRequest): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.baseUrl}/assign-roles`, request);
  }
}

