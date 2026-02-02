/* 
 * LED Studio Pro - Generated ESP32 Code
 * Spec: P5 (64x32 px)
 */

void drawUI() {
  // Item #1: TEXT
  dma_display->setTextSize(3);
  dma_display->setCursor(1, 4);
  dma_display->setTextColor(0XFF0000);
  dma_display->print("88");

  // Item #2: TEXT
  dma_display->setTextSize(3);
  dma_display->setCursor(34, 4);
  dma_display->setTextColor(0X1EFF00);
  dma_display->print("88");

  // Item #3: LINE
  dma_display->drawLine(31, 1, 31, 31, 0XFFFFFF);

}
