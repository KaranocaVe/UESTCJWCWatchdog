import multiprocessing
import time
import ctypes
import winsound
from check import check_new_grades

TIMEOUT = 20
INTERVAL = 1200

def run_check(return_dict):
    try:
        result = check_new_grades()
        return_dict['result'] = result
    except Exception as e:
        return_dict['error'] = str(e)

def notify_user(new_courses):
    # 播放提示音
    winsound.Beep(1000, 500)  # 频率1000Hz，持续500ms
    winsound.Beep(1200, 300)

    # 弹窗通知（Windows MessageBox）
    msg = "检测到新成绩：\n\n" + "\n".join(
        f"{course['课程名称']}：{course.get('最终', '无成绩')}" for course in new_courses
    )
    ctypes.windll.user32.MessageBoxW(0, msg, "成绩更新提醒", 0x40)  # 0x40 是信息图标

def main_loop():
    while True:
        print(f"\n开始一次成绩检查：{time.strftime('%Y-%m-%d %H:%M:%S')}")
        manager = multiprocessing.Manager()
        return_dict = manager.dict()

        p = multiprocessing.Process(target=run_check, args=(return_dict,))
        p.start()
        p.join(timeout=TIMEOUT)

        if p.is_alive():
            print(f"超过 {TIMEOUT} 秒未返回，终止子进程")
            p.terminate()
            p.join()
        else:
            if 'result' in return_dict:
                new_courses = return_dict['result']
                if new_courses:
                    print("发现新增课程：")
                    for course in new_courses:
                        print(course)
                    notify_user(new_courses)
                else:
                    print("没有新增课程")
            elif 'error' in return_dict:
                print("查询出错：", return_dict['error'])

        time.sleep(INTERVAL)

if __name__ == "__main__":
    multiprocessing.freeze_support()
    main_loop()
