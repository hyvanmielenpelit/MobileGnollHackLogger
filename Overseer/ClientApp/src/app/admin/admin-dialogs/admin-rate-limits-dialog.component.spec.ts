import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminRateLimitsDialogComponent } from './admin-rate-limits-dialog.component';
import { configureAdminTestBed } from '../admin.component.testing';

describe('AdminRateLimitsDialogComponent', () => {
  let component: AdminRateLimitsDialogComponent;
  let fixture: ComponentFixture<AdminRateLimitsDialogComponent>;

  beforeEach(async () => {
    await configureAdminTestBed(AdminRateLimitsDialogComponent);
    fixture = TestBed.createComponent(AdminRateLimitsDialogComponent);
    component = fixture.componentInstance;
  });

  describe('rate limits dialog tab row', () => {
    it('should wrap around and move selection with the arrow keys', () => {
      component.activeRateLimitTab = 'chat';

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      expect(component.activeRateLimitTab).toBe('title');

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 1);
      expect(component.activeRateLimitTab).toBe('chat');
    });

    it('should select the ends with Home and End', () => {
      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
      expect(component.activeRateLimitTab).toBe('title');

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 1);
      expect(component.activeRateLimitTab).toBe('chat');
    });

    it('should ignore unrelated keys', () => {
      component.activeRateLimitTab = 'title';
      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'Enter' }), 1);
      expect(component.activeRateLimitTab).toBe('title');
    });
  });
});
