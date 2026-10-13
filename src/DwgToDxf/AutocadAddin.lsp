;;; AutocadAddin.lsp
;;; Nap DwgToDxf.dll khi go lenh lan dau, khong can NETLOAD bang tay.
;;;
;;; Cach dung:
;;;   1. De file nay cung thu muc voi DwgToDxf.dll.
;;;   2. Them thu muc do vao Support File Search Path va Trusted Locations
;;;      (OPTIONS > Files), HOAC sua duong dan *ACA-DLL* ben duoi.
;;;   3. APPLOAD > Startup Suite > Contents... > Add file nay, de tu nap moi lan mo AutoCAD.
;;;   4. Go DWG2DXF, -DWG2DXF, CAIDATIN, INMODEL, INLAYOUT, INNHIEUFILE nhu binh thuong.
;;;
;;; Lan dau go lenh, file nay nap DLL roi chay lenh. Tu lan sau AutoCAD chay thang lenh trong DLL.

(vl-load-com)

;; Duong dan day du toi DLL. De nil thi tim DwgToDxf.dll trong Support File Search Path.
;; Vi du: (setq *ACA-DLL* "D:/AutocadAddin/DwgToDxf.dll")
(if (not (boundp '*ACA-DLL*)) (setq *ACA-DLL* nil))

(defun aca:dll-path ()
  (cond
    ((and *ACA-DLL* (findfile *ACA-DLL*)))
    ((findfile "DwgToDxf.dll"))
  )
)

;; Nap DLL mot lan cho ca phien AutoCAD (bien blackboard dung chung moi ban ve).
(defun aca:load (/ dll)
  (cond
    ((vl-bb-ref '*ACA-LOADED*) T)
    ((setq dll (aca:dll-path))
     (command "_.NETLOAD" dll)
     (vl-bb-set '*ACA-LOADED* T)
     T
    )
    (T
     (princ "\nKhong tim thay DwgToDxf.dll. Them thu muc chua DLL vao Support File Search Path, hoac sua *ACA-DLL* trong AutocadAddin.lsp.")
     nil
    )
  )
)

;; Nap DLL roi gui lai lenh. Lenh trong DLL duoc uu tien hon ham LISP cung ten.
(defun aca:run (cmd)
  (if (aca:load)
    (vla-SendCommand (vla-get-ActiveDocument (vlax-get-acad-object)) (strcat cmd " "))
  )
  (princ)
)

(defun c:DWG2DXF () (aca:run "DWG2DXF"))
(defun c:-DWG2DXF () (aca:run "-DWG2DXF"))
(defun c:INMODEL () (aca:run "INMODEL"))
(defun c:INLAYOUT () (aca:run "INLAYOUT"))
(defun c:CAIDATIN () (aca:run "CAIDATIN"))
(defun c:INNHIEUFILE () (aca:run "INNHIEUFILE"))
(defun c:INNHIEUFILECHAY () (aca:run "INNHIEUFILECHAY"))

;; Lenh nap ngay, khong chay lenh nao.
(defun c:TAIADDIN () (if (aca:load) (princ "\nDa nap AutocadAddin.")) (princ))

(princ "\nAutocadAddin: go CAIDATIN, INMODEL, INLAYOUT, INNHIEUFILE, DWG2DXF (DLL se tu nap lan dau).")
(princ)
