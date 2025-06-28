import threading
import time
import ctypes
import winsound
from check import *

TIMEOUT = 20
INTERVAL = 3600  # 每60分钟检查一次
DEVFLAG = True


def run_check(driver,return_dict,semister="463"):
    try:
        result = check_new_grades(driver,semister)
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
    print("创建浏览器并登录")
    driver = create_driver_and_login(dev=DEVFLAG)
    semister_id = determine_semister_id(driver)
    while True:
        print(f"\n开始一次成绩检查：{time.strftime('%Y-%m-%d %H:%M:%S')}")
        return_dict = {}

        p = threading.Thread(target=run_check,args=(driver,return_dict,semister_id,))
        p.start()
        p.join(timeout=TIMEOUT)

        if p.is_alive():
            print(f"超过 {TIMEOUT} 秒未返回，终止子进程")
            p.terminate()
            p.join()
        else:
            if 'result' in return_dict:
                new_courses = return_dict['result']
                if new_courses and {} not in new_courses:
                    print("发现新增课程：")
                    for course in new_courses:
                        print(course)
                    notify_user(new_courses)
                else:
                    print("没有新增课程")
            elif 'error' in return_dict:
                print("查询出错：", return_dict['error'])
                print("重新创建浏览器并登录")
                driver.quit()
                driver = create_driver_and_login()

        time.sleep(INTERVAL)


if __name__ == "__main__":
    if DEVFLAG:
        TIMEOUT = 2000
        INTERVAL = 10
    main_loop()
