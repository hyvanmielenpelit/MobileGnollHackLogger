import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DebugLogComponent } from './debug-log.component';

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
});
