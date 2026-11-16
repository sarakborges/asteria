import {
  useCallback, useLayoutEffect, useRef, useState,
  type ReactNode, type Ref, type UIEventHandler,
} from "react";
import "./ScrollArea.css";

type Metrics = { height: number; scrollHeight: number; scrollTop: number };
const MIN_THUMB_PX = 28;
const OVERFLOW_TOLERANCE_PX = 0.5;

export type ScrollAreaProps = {
  children: ReactNode;
  className?: string;
  contentClassName?: string;
  viewportClassName?: string;
  viewportRef?: Ref<HTMLDivElement>;
  onViewportScroll?: UIEventHandler<HTMLDivElement>;
  ariaLabel: string;
};

/**
 * Native viewport owns wheel, touch, keyboard navigation and scroll position.
 * The measured visual scrollbar only mirrors that position. A ResizeObserver
 * checks actual laid-out overflow, not element counts or estimated text lines.
 */
export function ScrollArea({
  children, className, contentClassName, viewportClassName,
  viewportRef: forwardedViewportRef, onViewportScroll, ariaLabel,
}: ScrollAreaProps) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ pointer: number; y: number; top: number } | null>(null);
  const [metrics, setMetrics] = useState<Metrics>({
    height: 0, scrollHeight: 0, scrollTop: 0,
  });

  const assignViewport = useCallback((element: HTMLDivElement | null) => {
    viewportRef.current = element;
    if (typeof forwardedViewportRef === "function") forwardedViewportRef(element);
    else if (forwardedViewportRef) forwardedViewportRef.current = element;
  }, [forwardedViewportRef]);

  const measure = useCallback(() => {
    const element = viewportRef.current;
    if (!element) return;
    const next = {
      height: element.clientHeight,
      scrollHeight: element.scrollHeight,
      scrollTop: element.scrollTop,
    };
    setMetrics(previous =>
      previous.height === next.height &&
      previous.scrollHeight === next.scrollHeight &&
      previous.scrollTop === next.scrollTop ? previous : next);
  }, []);

  useLayoutEffect(() => {
    const viewport = viewportRef.current;
    const content = contentRef.current;
    if (!viewport || !content) return;
    const observer = new ResizeObserver(measure);
    observer.observe(viewport);
    observer.observe(content);
    measure();
    return () => observer.disconnect();
  }, [measure]);

  const overflow = Math.max(0, metrics.scrollHeight - metrics.height);
  const hasScrollbar = overflow > OVERFLOW_TOLERANCE_PX && metrics.height > 0;
  const thumbHeight = hasScrollbar
    ? Math.min(metrics.height, Math.max(MIN_THUMB_PX,
      metrics.height * metrics.height / metrics.scrollHeight))
    : 0;
  const available = Math.max(0, metrics.height - thumbHeight);
  const thumbTop = overflow > 0
    ? Math.min(available, Math.max(0, metrics.scrollTop / overflow * available))
    : 0;

  const scrollToPointer = (pointerY: number, trackTop: number) => {
    if (!viewportRef.current) return;
    const fraction = Math.min(1, Math.max(0,
      (pointerY - trackTop - thumbHeight / 2) / Math.max(1, available)));
    viewportRef.current.scrollTop = fraction * overflow;
    measure();
  };

  return (
    <div className={["ui-scroll-area", hasScrollbar ? "ui-scroll-area--overflow" : "",
      className].filter(Boolean).join(" ")}>
      <div ref={assignViewport}
        className={["ui-scroll-area__viewport", viewportClassName]
          .filter(Boolean).join(" ")}
        role="region" aria-label={ariaLabel} tabIndex={0}
        onScroll={event => {
          measure();
          onViewportScroll?.(event);
        }}>
        <div ref={contentRef} className={["ui-scroll-area__content", contentClassName]
          .filter(Boolean).join(" ")}>{children}</div>
      </div>
      {hasScrollbar && (
        <div className="ui-scroll-area__track" aria-hidden="true"
          onPointerDown={event => {
            if (event.button !== 0 || event.target !== event.currentTarget)
              return;
            scrollToPointer(event.clientY,
              event.currentTarget.getBoundingClientRect().top);
          }}>
          <div className="ui-scroll-area__thumb"
            style={{ height: thumbHeight, transform: `translateY(${thumbTop}px)` }}
            onPointerDown={event => {
              if (event.button !== 0 || !viewportRef.current) return;
              event.preventDefault();
              event.stopPropagation();
              dragRef.current = {
                pointer: event.pointerId, y: event.clientY,
                top: viewportRef.current.scrollTop,
              };
              event.currentTarget.setPointerCapture(event.pointerId);
            }}
            onPointerMove={event => {
              const drag = dragRef.current;
              if (!drag || drag.pointer !== event.pointerId || !viewportRef.current) return;
              viewportRef.current.scrollTop = Math.min(overflow, Math.max(0,
                drag.top + (event.clientY - drag.y) * overflow /
                  Math.max(1, available)));
            }}
            onPointerUp={event => {
              if (dragRef.current?.pointer !== event.pointerId) return;
              dragRef.current = null;
              if (event.currentTarget.hasPointerCapture(event.pointerId))
                event.currentTarget.releasePointerCapture(event.pointerId);
            }}
            onPointerCancel={() => { dragRef.current = null; }}
          />
        </div>
      )}
    </div>
  );
}
