import type { Mock } from 'vitest';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { AdminAlertsComponent } from './admin-alerts.component';
import { AdminAlertService, SystemAlert } from '../services/admin-alert.service';

@Component({ selector: 'app-admin-stub', template: '' })
class AdminStubComponent {}

describe('AdminAlertsComponent', () => {
  let fixture: ComponentFixture<AdminAlertsComponent>;
  let alerts: BehaviorSubject<SystemAlert[]>;
  let dismiss: Mock;

  const alert = (overrides: Partial<SystemAlert> = {}): SystemAlert => ({
    id: 'missing-config', type: 'warning', message: '2 system configurations use models that are not in the catalog.', ...overrides
  });

  const el = (): HTMLElement => fixture.nativeElement;
  const items = () => Array.from(el().querySelectorAll<HTMLElement>('.admin-alert'));
  const link = () => el().querySelector<HTMLAnchorElement>('a.admin-alert-link');

  beforeEach(async () => {
    alerts = new BehaviorSubject<SystemAlert[]>([]);
    dismiss = vi.fn((id: string) => alerts.next(alerts.value.filter(a => a.id !== id)));

    await TestBed.configureTestingModule({
      imports: [AdminAlertsComponent],
      providers: [
        provideRouter([{ path: 'admin', component: AdminStubComponent }]),
        { provide: AdminAlertService, useValue: { alerts$: alerts.asObservable(), dismiss } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminAlertsComponent);
    fixture.detectChanges();
  });

  it('labels the alert with a hidden glyph, the words "Admin alert" and the severity', () => {
    alerts.next([alert()]);
    fixture.detectChanges();

    const card = items()[0];
    const label = card.querySelector('.admin-alert-label')!;
    expect(label.textContent!.trim()).toBe('Admin alert');
    expect(card.querySelector('.admin-alert-glyph svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(card.querySelector('.admin-alert-severity')!.textContent!.trim()).toBe('Warning');
    expect(card.getAttribute('data-severity')).toBe('warning');
    expect(card.getAttribute('aria-labelledby')).toBe(label.id);
    expect(card.textContent).toContain('2 system configurations use models that are not in the catalog.');
    expect(link()).toBeNull();
  });

  it('marks an error alert with its own glyph and word', () => {
    alerts.next([alert({ id: 'e', type: 'error' }), alert({ id: 'n', type: 'notice' })]);
    fixture.detectChanges();

    const [error, unknown] = items();
    expect(error.getAttribute('data-severity')).toBe('error');
    expect(error.querySelector('.admin-alert-severity')!.textContent!.trim()).toBe('Error');
    expect(error.querySelector('.admin-alert-glyph svg circle')).not.toBeNull();
    expect(unknown.getAttribute('data-severity')).toBe('warning');
    expect(unknown.querySelector('.admin-alert-severity')!.textContent!.trim()).toBe('Warning');
    expect(unknown.querySelector('.admin-alert-glyph svg circle')).toBeNull();
  });

  it('renders the link after the message and navigates with its query parameter intact', async () => {
    alerts.next([alert({ linkUrl: '/admin?tab=configs', linkText: 'Review in System Configs' })]);
    fixture.detectChanges();

    const anchor = link()!;
    expect(anchor.textContent!.trim()).toBe('Review in System Configs');
    expect(anchor.getAttribute('href')).toBe('/admin?tab=configs');
    expect(anchor.parentElement!.classList).toContain('admin-alert-actions');
    expect(anchor.querySelector('svg.admin-alert-link-arrow')!.getAttribute('aria-hidden')).toBe('true');
    const content = items()[0].querySelector('.admin-alert-body')!.textContent!;
    expect(content.indexOf('not in the catalog.')).toBeLessThan(content.indexOf('Review in System Configs'));

    anchor.click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/admin?tab=configs');
  });

  it('renders no link without link text, or for a link off the site', () => {
    alerts.next([
      alert({ id: 'a', linkUrl: '/admin?tab=configs', linkText: null }),
      alert({ id: 'b', linkUrl: 'https://example.com/admin', linkText: 'Open' }),
      alert({ id: 'c', linkUrl: '//example.com/admin', linkText: 'Open' })
    ]);
    fixture.detectChanges();

    expect(items().length).toBe(3);
    expect(link()).toBeNull();
  });

  it('dismisses an alert from its named button', () => {
    alerts.next([alert({ id: 'first' }), alert({ id: 'second', message: 'Another alert.' })]);
    fixture.detectChanges();

    const button = items()[0].querySelector<HTMLButtonElement>('.dismiss-btn')!;
    expect(button.getAttribute('type')).toBe('button');
    expect(button.classList).toContain('action-btn');
    expect(button.getAttribute('aria-label')).toBe('Dismiss alert');
    const description = el().querySelector<HTMLElement>('#' + button.getAttribute('aria-describedby'))!;
    expect(description.matches('p.admin-alert-message')).toBe(true);
    expect(description.textContent!.trim()).toBe('2 system configurations use models that are not in the catalog.');
    expect(button.hasAttribute('title')).toBe(false);
    expect(el().querySelector('#' + button.getAttribute('interestfor'))!.getAttribute('popover')).toBe('hint');

    button.click();
    fixture.detectChanges();

    expect(dismiss).toHaveBeenCalledWith('first');
    expect(items().length).toBe(1);
    expect(items()[0].textContent).toContain('Another alert.');
  });

  it('shows the container while there are alerts and hides it when the last is dismissed', () => {
    const container = el().querySelector<HTMLElement>('#admin-alerts-popover')!;
    expect(container.matches(':popover-open')).toBe(false);

    alerts.next([alert()]);
    fixture.detectChanges();
    expect(container.matches(':popover-open')).toBe(true);

    items()[0].querySelector<HTMLButtonElement>('.dismiss-btn')!.click();
    fixture.detectChanges();
    expect(container.matches(':popover-open')).toBe(false);
  });
});
