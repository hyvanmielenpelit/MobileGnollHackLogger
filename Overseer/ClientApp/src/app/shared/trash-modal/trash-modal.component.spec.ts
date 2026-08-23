import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { TrashModalComponent } from './trash-modal.component';
import { ChatService, TrashSession } from '../../services/chat.service';

describe('TrashModalComponent', () => {
  let component: TrashModalComponent;
  let fixture: ComponentFixture<TrashModalComponent>;
  let chatService: jasmine.SpyObj<ChatService>;

  const mockTrashSessions: TrashSession[] = [
    {
      id: 101,
      title: 'Old Deleted Chat',
      createdUtc: '2026-07-01T00:00:00Z',
      lastMessageUtc: '2026-07-01T00:00:00Z',
      deletedUtc: '2026-08-01T00:00:00Z',
      daysRemaining: 20,
      deletionReason: 'user_action',
      isPinned: false,
      messageCount: 5
    },
    {
      id: 102,
      title: 'Another Trash Chat',
      createdUtc: '2026-07-10T00:00:00Z',
      lastMessageUtc: '2026-07-10T00:00:00Z',
      deletedUtc: '2026-08-10T00:00:00Z',
      daysRemaining: 29,
      deletionReason: 'quota_eviction',
      isPinned: false,
      messageCount: 2
    }
  ];

  beforeEach(async () => {
    const chatServiceSpy = jasmine.createSpyObj('ChatService', [
      'getTrashSessions',
      'restoreSession',
      'permanentDeleteSession',
      'emptyTrash'
    ]);
    chatServiceSpy.getTrashSessions.and.returnValue(of(mockTrashSessions));

    await TestBed.configureTestingModule({
      imports: [TrashModalComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ChatService, useValue: chatServiceSpy }
      ]
    }).compileComponents();

    chatService = TestBed.inject(ChatService) as jasmine.SpyObj<ChatService>;
    fixture = TestBed.createComponent(TrashModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load trash on init', () => {
    expect(component).toBeTruthy();
    expect(chatService.getTrashSessions).toHaveBeenCalledWith('');
    expect(component.trashSessions.length).toBe(2);
  });

  it('should open and close modal dialog', () => {
    const showModalSpy = jasmine.createSpy('showModal');
    const closeSpy = jasmine.createSpy('close');
    component.trashDialog = { nativeElement: { showModal: showModalSpy, close: closeSpy } } as any;

    component.open();
    expect(showModalSpy).toHaveBeenCalled();

    component.close();
    expect(closeSpy).toHaveBeenCalled();
  });

  it('should restore trash session when not at max quota', () => {
    chatService.restoreSession.and.returnValue(of({ message: 'Session restored' }));
    const countChangeSpy = spyOn(component.trashCountChange, 'emit');
    const restoredSpy = spyOn(component.sessionRestored, 'emit');

    component.activeSessionCount = 10;
    component.maxQuota = 50;
    component.restoreTrashSession(101);

    expect(chatService.restoreSession).toHaveBeenCalledWith(101);
    expect(component.trashSessions.length).toBe(1);
    expect(countChangeSpy).toHaveBeenCalledWith(1);
    expect(restoredSpy).toHaveBeenCalledWith(101);
  });

  it('should not restore trash session when at max quota', () => {
    component.activeSessionCount = 50;
    component.maxQuota = 50;

    component.restoreTrashSession(101);
    expect(chatService.restoreSession).not.toHaveBeenCalled();
  });

  it('should emit restoreError when server returns error on restore', () => {
    chatService.restoreSession.and.returnValue(
      throwError(() => ({ error: { message: 'Quota exceeded' } }))
    );
    const errorSpy = spyOn(component.restoreError, 'emit');

    component.activeSessionCount = 10;
    component.maxQuota = 50;
    component.restoreTrashSession(101);

    expect(errorSpy).toHaveBeenCalledWith('Quota exceeded');
  });

  it('should permanently delete a session', () => {
    chatService.permanentDeleteSession.and.returnValue(of({ message: 'Deleted' }));
    const showModalSpy = jasmine.createSpy('showModal');
    const closeSpy = jasmine.createSpy('close');
    component.permanentDeleteConfirmDialog = { nativeElement: { showModal: showModalSpy, close: closeSpy } } as any;
    const countChangeSpy = spyOn(component.trashCountChange, 'emit');

    component.requestPermanentDelete(101);
    expect(component.trashSessionToDeletePermanently).toBe(101);
    expect(showModalSpy).toHaveBeenCalled();

    component.confirmPermanentDelete();
    expect(chatService.permanentDeleteSession).toHaveBeenCalledWith(101);
    expect(component.trashSessions.length).toBe(1);
    expect(countChangeSpy).toHaveBeenCalledWith(1);
    expect(closeSpy).toHaveBeenCalled();
  });

  it('should empty entire trash', () => {
    chatService.emptyTrash.and.returnValue(of({ count: 2 }));
    const showModalSpy = jasmine.createSpy('showModal');
    const closeSpy = jasmine.createSpy('close');
    component.emptyTrashConfirmDialog = { nativeElement: { showModal: showModalSpy, close: closeSpy } } as any;
    const countChangeSpy = spyOn(component.trashCountChange, 'emit');
    const trashEmptiedSpy = spyOn(component.trashEmptied, 'emit');

    component.requestEmptyTrash();
    expect(showModalSpy).toHaveBeenCalled();

    component.confirmEmptyTrash();

    expect(chatService.emptyTrash).toHaveBeenCalled();
    expect(component.trashSessions.length).toBe(0);
    expect(countChangeSpy).toHaveBeenCalledWith(0);
    expect(trashEmptiedSpy).toHaveBeenCalled();
    expect(closeSpy).toHaveBeenCalled();
  });
});
