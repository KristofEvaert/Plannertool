import { ApplicationConfig } from '@angular/core'
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async'
import { provideRouter, withComponentInputBinding } from '@angular/router'
import { providePrimeNG } from 'primeng/config'

import { provideHttpClient, withInterceptors } from '@angular/common/http'
import { authInterceptor } from '@utils/auth-interceptor'
import { MessageService } from 'primeng/api'
import { routes } from './app.routes'
import { primePreset } from './primePreset'

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(withInterceptors([authInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
    provideAnimationsAsync(),
    providePrimeNG({
      theme: {
        preset: primePreset,
        options: {
          darkModeSelector: '.dark',
        },
      },
      ripple: true,
      translation: {
        startsWith: 'Starts With',
        contains: 'Contains',
        notContains: 'Does Not Contain',
        endsWith: 'Ends With',
        equals: 'Equals',
        notEquals: 'Does Not Equal',
      },
    }),
    MessageService,
  ],
}
