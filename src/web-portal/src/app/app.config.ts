import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { requestLogInterceptor } from './request-log';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()), // route params (e.g. :id) become component inputs
    provideHttpClient(withFetch(), withInterceptors([requestLogInterceptor])), // feeds the request inspector
  ],
};
