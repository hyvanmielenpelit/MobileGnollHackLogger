import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DebugLogComponent } from './debug-log.component';
import { ClientBridgeService } from '../services/client-bridge.service';

describe('DebugLogComponent', () => {
  let component: DebugLogComponent;
  let fixture: ComponentFixture<DebugLogComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DebugLogComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DebugLogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should post message via ClientBridgeService when shareLogs is called in webview', () => {
    const bridge = TestBed.inject(ClientBridgeService);
    spyOn(bridge, 'isEmbedded').and.returnValue(true);
    const postSpy = spyOn(bridge, 'postMessage');

    component.shareLogs();

    expect(postSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      type: 'share_text_file',
      filename: 'overseer-debug-log.txt'
    }));
    expect(component.isShared).toBeTrue();
  });
});
